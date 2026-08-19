using System.Collections.Concurrent;
using FlightBoard.Core.Storage;
using Microsoft.Extensions.Logging;

namespace FlightBoard.Core.Enrichment;

/// <summary>
/// Builds an <see cref="Enriched"/> for a flight: known-routes config → adsbdb → hexdb → airline prefix table,
/// with everything cached in SQLite and in-flight requests de-duplicated so prefetch and show never double-hit the APIs.
/// </summary>
public sealed class CachedEnricher : IFlightEnricher
{
    private readonly AdsbdbClient _adsbdb;
    private readonly HexdbClient _hexdb;
    private readonly LookupCache<RouteInfo> _routes;
    private readonly LookupCache<AircraftInfo> _aircraft;
    private readonly Dictionary<string, KnownRoute> _known;
    private readonly Dictionary<string, string> _airportNames;
    private readonly ILogger<CachedEnricher> _log;
    private readonly ConcurrentDictionary<string, Lazy<Task<Enriched>>> _inflight = new(StringComparer.OrdinalIgnoreCase);

    public CachedEnricher(Db db, AdsbdbClient adsbdb, HexdbClient hexdb, EnrichmentOptions options, ILogger<CachedEnricher> log)
    {
        _adsbdb = adsbdb;
        _hexdb = hexdb;
        _log = log;
        var hit = TimeSpan.FromDays(Math.Max(1, options.CacheDays));
        var miss = TimeSpan.FromHours(Math.Max(1, options.NotFoundCacheHours));
        _routes = new LookupCache<RouteInfo>(db, "RouteCache", "Callsign", hit, miss);
        _aircraft = new LookupCache<AircraftInfo>(db, "AircraftCache", "Hex", hit, miss);
        _known = options.KnownRoutes.Where(k => !string.IsNullOrWhiteSpace(k.Callsign))
            .ToDictionary(k => k.Callsign.Trim(), StringComparer.OrdinalIgnoreCase);
        _airportNames = new Dictionary<string, string>(options.AirportDisplayNames, StringComparer.OrdinalIgnoreCase);
    }

    public Task<Enriched> EnrichAsync(string hex, string? callsign, CancellationToken ct)
    {
        var key = hex + "|" + (callsign ?? "");
        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<Enriched>>(() => EnrichCoreAsync(hex, callsign, ct)));
        var task = lazy.Value;
        task.ContinueWith(t => _inflight.TryRemove(key, out _), TaskScheduler.Default);
        return task;
    }

    private async Task<Enriched> EnrichCoreAsync(string hex, string? callsign, CancellationToken ct)
    {
        callsign = callsign?.Trim();
        RouteInfo? route = null;
        AircraftInfo? aircraft = null;

        if (!string.IsNullOrEmpty(callsign))
        {
            if (_known.TryGetValue(callsign, out var k))
            {
                route = new RouteInfo(callsign, k.FlightIata, k.AirlineIcao, k.AirlineName, k.OriginIcao, k.OriginIata, k.OriginCity, null,
                    k.OriginCountry, k.OriginLat, k.OriginLon, k.DestinationIcao, null, null);
            }
            else
            {
                route = await LookupRouteAsync(callsign, ct);
            }
        }

        aircraft = await LookupAircraftAsync(hex, ct);

        // Preferred display names win over whatever municipality the DBs hold.
        string? originCity = route?.OriginCity, destCity = route?.DestCity;
        if (route?.OriginIcao is { } oi && _airportNames.TryGetValue(oi, out var on)) originCity = on;
        if (route?.DestIcao is { } di && _airportNames.TryGetValue(di, out var dn)) destCity = dn;

        var prefix = AirlinePrefixTable.Lookup(callsign);
        var airlineIcao = Enriched.FirstNonEmpty(route?.AirlineIcao, prefix?.Icao, aircraft?.OperatorIcao);
        var airlineName = Enriched.FirstNonEmpty(route?.AirlineName, prefix?.Name, aircraft?.Owner);
        var flightIata = route?.FlightIata;
        if (string.IsNullOrEmpty(flightIata) && callsign is not null && prefix is not null)
            flightIata = AirlinePrefixTable.ToIataFlight(callsign, prefix.Iata);

        return new Enriched(
            Hex: hex,
            Callsign: callsign,
            FlightIata: flightIata,
            AirlineIcao: airlineIcao,
            AirlineName: airlineName,
            OriginIcao: route?.OriginIcao,
            OriginIata: route?.OriginIata,
            OriginCity: originCity,
            OriginName: route?.OriginName,
            OriginCountry: route?.OriginCountry,
            OriginLat: route?.OriginLat,
            OriginLon: route?.OriginLon,
            DestinationIcao: route?.DestIcao,
            DestinationCity: destCity,
            Registration: aircraft?.Registration,
            TypeIcao: aircraft?.TypeIcao,
            TypeName: aircraft?.TypeName,
            Owner: aircraft?.Owner,
            PhotoUrl: aircraft?.PhotoUrl,
            RouteFound: route?.OriginIcao is not null,
            AircraftFound: aircraft is not null);
    }

    private async Task<RouteInfo?> LookupRouteAsync(string callsign, CancellationToken ct)
    {
        var cached = _routes.Get(callsign);
        if (cached is not null) return cached.Value;

        try
        {
            var route = await _adsbdb.GetRouteAsync(callsign, ct);
            if (route is null)
            {
                var hx = await _hexdb.GetRouteAsync(callsign, ct);
                if (hx is { } r)
                {
                    var ap = await SafeAsync(() => _hexdb.GetAirportAsync(r.Origin, ct));
                    var prefix = AirlinePrefixTable.Lookup(callsign);
                    route = new RouteInfo(callsign, prefix is null ? null : AirlinePrefixTable.ToIataFlight(callsign, prefix.Iata),
                        prefix?.Icao, prefix?.Name, r.Origin, ap?.Iata, null, ap?.Name, ap?.Country, ap?.Lat, ap?.Lon, r.Dest, null, null); // hexdb has no city, only region
                }
            }
            _routes.Put(callsign, route);
            return route;
        }
        catch (EnrichmentUnavailableException)
        {
            return null; // transient: do not cache the miss
        }
    }

    private async Task<AircraftInfo?> LookupAircraftAsync(string hex, CancellationToken ct)
    {
        var cached = _aircraft.Get(hex);
        if (cached is not null) return cached.Value;
        try
        {
            var info = await _adsbdb.GetAircraftAsync(hex, ct) ?? await SafeAsync(() => _hexdb.GetAircraftAsync(hex, ct));
            _aircraft.Put(hex, info);
            return info;
        }
        catch (EnrichmentUnavailableException)
        {
            return null;
        }
    }

    private async Task<T?> SafeAsync<T>(Func<Task<T?>> f) where T : class
    {
        try { return await f(); }
        catch (EnrichmentUnavailableException) { return null; }
    }

}
