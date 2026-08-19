using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlightBoard.Core.Enrichment;

public sealed record RouteInfo(
    string Callsign, string? FlightIata, string? AirlineIcao, string? AirlineName,
    string? OriginIcao, string? OriginIata, string? OriginCity, string? OriginName, string? OriginCountry, double? OriginLat, double? OriginLon,
    string? DestIcao, string? DestIata, string? DestCity);

public sealed record AircraftInfo(
    string Hex, string? Registration, string? TypeIcao, string? TypeName, string? Manufacturer, string? Owner, string? OperatorIcao, string? PhotoUrl);

/// <summary>Free crowd-sourced callsign→route and hex→airframe lookups from adsbdb.com. Returns null on 404.</summary>
public sealed class AdsbdbClient
{
    private readonly HttpClient _http;
    private readonly string _base;
    private readonly ILogger<AdsbdbClient> _log;

    public AdsbdbClient(HttpClient http, EnrichmentOptions options, ILogger<AdsbdbClient> log)
    {
        _http = http;
        _base = options.AdsbdbBaseUrl.TrimEnd('/');
        _log = log;
    }

    public async Task<RouteInfo?> GetRouteAsync(string callsign, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{_base}/callsign/{Uri.EscapeDataString(callsign)}", ct);
        if (doc is null) return null;
        if (!doc.RootElement.TryGetProperty("response", out var resp) || !resp.TryGetProperty("flightroute", out var fr)) return null;
        var airline = Prop(fr, "airline");
        var origin = Prop(fr, "origin");
        var dest = Prop(fr, "destination");
        return new RouteInfo(
            Callsign: callsign,
            FlightIata: Str(fr, "callsign_iata"),
            AirlineIcao: Str(airline, "icao"),
            AirlineName: Str(airline, "name"),
            OriginIcao: Str(origin, "icao_code"),
            OriginIata: Str(origin, "iata_code"),
            OriginCity: Str(origin, "municipality"),
            OriginName: Str(origin, "name"),
            OriginCountry: Str(origin, "country_name"),
            OriginLat: Dbl(origin, "latitude"),
            OriginLon: Dbl(origin, "longitude"),
            DestIcao: Str(dest, "icao_code"),
            DestIata: Str(dest, "iata_code"),
            DestCity: Str(dest, "municipality"));
    }

    public async Task<AircraftInfo?> GetAircraftAsync(string hex, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{_base}/aircraft/{Uri.EscapeDataString(hex)}", ct);
        if (doc is null) return null;
        if (!doc.RootElement.TryGetProperty("response", out var resp) || !resp.TryGetProperty("aircraft", out var ac)) return null;
        return new AircraftInfo(
            Hex: hex,
            Registration: Str(ac, "registration"),
            TypeIcao: Str(ac, "icao_type"),
            TypeName: Str(ac, "type"),
            Manufacturer: Str(ac, "manufacturer"),
            Owner: Str(ac, "registered_owner"),
            OperatorIcao: Str(ac, "registered_owner_operator_flag_code"),
            PhotoUrl: Str(ac, "url_photo"));
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("adsbdb {Url} failed: {Message}", url, ex.Message);
            throw new EnrichmentUnavailableException(url, ex);
        }
    }

    private static JsonElement? Prop(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
    private static string? Str(JsonElement? el, string name) =>
        el is { } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Dbl(JsonElement? el, string name) =>
        el is { } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

/// <summary>Transient failure talking to a lookup service - do not cache the miss.</summary>
public sealed class EnrichmentUnavailableException(string url, Exception inner) : Exception($"Lookup unavailable: {url}", inner);
