using System.Globalization;
using System.Net;
using System.Text.Json;
using FlightBoard.Core.Geo;
using FlightBoard.Core.Model;
using Microsoft.Extensions.Logging;

namespace FlightBoard.Core.Sources;

/// <summary>
/// Polls any readsb/dump1090/ADSBx-v2 shaped JSON endpoint: adsb.lol, adsb.fi, a local receiver.
/// Handles both the {"ac":[...]} and {"aircraft":[...]} wrappers, rotates to the next URL on failure,
/// and backs off when rate limited.
/// </summary>
public sealed class ReadsbJsonSource : IAircraftSource
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<string> _urls;
    private readonly ILogger<ReadsbJsonSource> _log;
    private int _index;
    private readonly DateTimeOffset[] _backoffUntil;

    public ReadsbJsonSource(HttpClient http, SourceOptions options, GeoPoint home, ILogger<ReadsbJsonSource> log)
    {
        _http = http;
        _log = log;
        var nm = options.RadiusNm.ToString("0.#", CultureInfo.InvariantCulture);
        _urls = options.Urls.Select(u => u
            .Replace("{lat}", home.Lat.ToString("0.#####", CultureInfo.InvariantCulture))
            .Replace("{lon}", home.Lon.ToString("0.#####", CultureInfo.InvariantCulture))
            .Replace("{nm}", nm)).ToList();
        if (_urls.Count == 0) throw new ArgumentException("At least one source URL is required", nameof(options));
        _backoffUntil = new DateTimeOffset[_urls.Count];
    }

    public string Name => "readsb:" + new Uri(_urls[_index]).Host;
    public string CurrentUrl => _urls[_index];

    public async Task<SourcePoll> PollAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < _urls.Count; attempt++)
        {
            var url = _urls[_index];
            if (DateTimeOffset.UtcNow < _backoffUntil[_index]) { Rotate(); continue; }   // this one is cooling off; try the next
            try
            {
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retry = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                    _log.LogWarning("{Url} rate limited; backing off {Delay}s and rotating", url, retry.TotalSeconds);
                    _backoffUntil[_index] = DateTimeOffset.UtcNow + retry;
                    Rotate();
                    continue;
                }
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var list = Parse(doc.RootElement);
                return new SourcePoll(DateTimeOffset.UtcNow, list);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                _log.LogWarning("Source {Url} failed: {Message}; rotating", url, ex.Message);
                _backoffUntil[_index] = DateTimeOffset.UtcNow.AddSeconds(5);
                Rotate();
            }
        }
        return new SourcePoll(DateTimeOffset.UtcNow, []);   // every source is failing or cooling off
    }

    private void Rotate() => _index = (_index + 1) % _urls.Count;

    public static List<AircraftSample> Parse(JsonElement root)
    {
        var result = new List<AircraftSample>();
        if (root.TryGetProperty("ac", out var arr) || root.TryGetProperty("aircraft", out arr))
        {
            foreach (var el in arr.EnumerateArray())
            {
                var s = ParseOne(el);
                if (s is not null) result.Add(s);
            }
        }
        return result;
    }

    public static AircraftSample? ParseOne(JsonElement el)
    {
        var hex = Str(el, "hex");
        if (string.IsNullOrEmpty(hex)) return null;
        var (altFt, onGround) = Alt(el);
        return new AircraftSample(
            Hex: hex.ToLowerInvariant(),
            Callsign: Str(el, "flight")?.Trim(),
            Registration: Str(el, "r"),
            Type: Str(el, "t"),
            Description: Str(el, "desc"),
            Lat: Dbl(el, "lat"),
            Lon: Dbl(el, "lon"),
            AltBaroFt: altFt,
            OnGround: onGround,
            GroundSpeedKt: Dbl(el, "gs"),
            TrackDeg: Dbl(el, "track") ?? Dbl(el, "true_heading"),
            BaroRateFpm: Int(el, "baro_rate") ?? Int(el, "geom_rate"),
            Squawk: Str(el, "squawk"),
            Emergency: Str(el, "emergency"),
            Category: Str(el, "category"),
            DbFlags: Int(el, "dbFlags") ?? 0,
            SeenPosSeconds: Dbl(el, "seen_pos") ?? 0,
            SeenSeconds: Dbl(el, "seen") ?? 0);
    }

    private static (int? alt, bool ground) Alt(JsonElement el)
    {
        if (!el.TryGetProperty("alt_baro", out var a)) return (Int(el, "alt_geom"), false);
        if (a.ValueKind == JsonValueKind.String) return (0, string.Equals(a.GetString(), "ground", StringComparison.OrdinalIgnoreCase));
        if (a.ValueKind == JsonValueKind.Number) return ((int)Math.Round(a.GetDouble()), false);
        return (null, false);
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Dbl(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? Int(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : null;
}
