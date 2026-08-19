using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlightBoard.Core.Enrichment;

public sealed record AirportInfo(string Icao, string? Iata, string? Name, string? Region, string? Country, double? Lat, double? Lon);

/// <summary>Fallback lookups from hexdb.io: callsign→"EGKK-LEAL" route strings, airport details, airframe by hex.</summary>
public sealed class HexdbClient
{
    private readonly HttpClient _http;
    private readonly string _base;
    private readonly ILogger<HexdbClient> _log;

    public HexdbClient(HttpClient http, EnrichmentOptions options, ILogger<HexdbClient> log)
    {
        _http = http;
        _base = options.HexdbBaseUrl.TrimEnd('/');
        _log = log;
    }

    /// <summary>Returns (originIcao, destIcao) or null.</summary>
    public async Task<(string Origin, string Dest)?> GetRouteAsync(string callsign, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{_base}/route/icao/{Uri.EscapeDataString(callsign)}", ct);
        var route = doc?.RootElement.TryGetProperty("route", out var r) == true ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(route)) return null;
        var parts = route.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Multi-leg routes look like "EGKK-LEAL-EGKK"; the leg ending at our airport is what matters, but
        // without knowing which leg we are on, first→last is the honest answer.
        return parts.Length >= 2 ? (parts[0], parts[^1]) : null;
    }

    public async Task<AirportInfo?> GetAirportAsync(string icao, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{_base}/airport/icao/{Uri.EscapeDataString(icao)}", ct);
        if (doc is null) return null;
        var e = doc.RootElement;
        return new AirportInfo(icao, Str(e, "iata"), Str(e, "airport"), Str(e, "region_name"), Str(e, "country_code"), Dbl(e, "latitude"), Dbl(e, "longitude"));
    }

    public async Task<AircraftInfo?> GetAircraftAsync(string hex, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{_base}/aircraft/{Uri.EscapeDataString(hex)}", ct);
        if (doc is null) return null;
        var e = doc.RootElement;
        return new AircraftInfo(hex, Str(e, "Registration"), Str(e, "ICAOTypeCode"), Str(e, "Type"), Str(e, "Manufacturer"),
            Str(e, "RegisteredOwners"), Str(e, "OperatorFlagCode"), null);
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            // hexdb answers some misses with 200 + {"error": ...}
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out _)) { doc.Dispose(); return null; }
            return doc;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("hexdb {Url} failed: {Message}", url, ex.Message);
            throw new EnrichmentUnavailableException(url, ex);
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Dbl(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
