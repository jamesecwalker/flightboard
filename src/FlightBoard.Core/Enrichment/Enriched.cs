namespace FlightBoard.Core.Enrichment;

/// <summary>Everything we could find out about a flight beyond what ADS-B broadcasts.</summary>
public sealed record Enriched(
    string Hex,
    string? Callsign,
    string? FlightIata,
    string? AirlineIcao,
    string? AirlineName,
    string? OriginIcao,
    string? OriginIata,
    string? OriginCity,
    string? OriginName,
    string? OriginCountry,
    double? OriginLat,
    double? OriginLon,
    string? DestinationIcao,
    string? DestinationCity,
    string? Registration,
    string? TypeIcao,
    string? TypeName,
    string? Owner,
    string? PhotoUrl,
    bool RouteFound,
    bool AircraftFound)
{
    public static Enriched Empty(string hex, string? callsign) =>
        new(hex, callsign, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, false);

    /// <summary>What to show as the "from" text: city first, then a shortened airport name, then IATA/ICAO code.</summary>
    public string? OriginDisplay => TrimAtSlash(FirstNonEmpty(OriginCity, ShortenAirportName(OriginName), OriginIata, OriginIcao));

    /// <summary>"Montpellier/Méditerranée" → "Montpellier"; "Paris/Charles de Gaulle" → "Paris".</summary>
    private static string? TrimAtSlash(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var i = s.IndexOf('/');
        return i > 2 ? s[..i].Trim() : s;
    }

    public string? DestinationDisplay => TrimAtSlash(FirstNonEmpty(DestinationCity, DestinationIcao));

    /// <summary>True when the route says this flight started at <paramref name="homeAirportIcao"/> - i.e. it is departing, not arriving.</summary>
    public bool IsDepartureFrom(string? homeAirportIcao) =>
        RouteFound && !string.IsNullOrEmpty(homeAirportIcao) && string.Equals(OriginIcao, homeAirportIcao, StringComparison.OrdinalIgnoreCase);

    private static readonly string[] AirportNoise = ["International", "Intl", "Airport", "Airfield", "Aerodrome", "Regional"];

    /// <summary>"Pisa International Airport" → "Pisa"; "Rome–Fiumicino Leonardo da Vinci International Airport" → "Rome–Fiumicino Leonardo da Vinci".</summary>
    public static string? ShortenAirportName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !AirportNoise.Contains(w.Trim(',', '.', '(', ')'), StringComparer.OrdinalIgnoreCase)).ToList();
        var shortened = string.Join(' ', words).Trim(' ', ',', '-');
        return shortened.Length == 0 ? name : shortened;
    }

    public static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public interface IFlightEnricher
{
    Task<Enriched> EnrichAsync(string hex, string? callsign, CancellationToken ct);
}

public sealed class EnrichmentOptions
{
    public string AdsbdbBaseUrl { get; set; } = "https://api.adsbdb.com/v0";
    public string HexdbBaseUrl { get; set; } = "https://hexdb.io/api/v1";
    public int CacheDays { get; set; } = 7;
    /// <summary>Negative results are retried sooner - the crowd-sourced DBs fill in over time.</summary>
    public int NotFoundCacheHours { get; set; } = 12;
    /// <summary>Hand-maintained routes that beat the online lookups. Handy for the simulator and for regulars the DBs get wrong.</summary>
    public List<KnownRoute> KnownRoutes { get; set; } = [];
    /// <summary>
    /// Airport ICAO → what the board should call it. The route DBs give the municipality, which for island airports
    /// is a town nobody uses ("Castletown" for the Isle of Man, "Saint Helier" for Jersey).
    /// </summary>
    public Dictionary<string, string> AirportDisplayNames { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EGNS"] = "Isle of Man", ["EGJJ"] = "Jersey", ["EGJB"] = "Guernsey", ["EGJA"] = "Alderney",
        ["TGPY"] = "Grenada", ["TLPL"] = "St Lucia", ["TAPA"] = "Antigua", ["TBPB"] = "Barbados", ["TKPK"] = "St Kitts",
        ["MKJS"] = "Montego Bay", ["TNCM"] = "St Maarten", ["TTPP"] = "Trinidad", ["MYNN"] = "Nassau",
        ["LFMN"] = "Nice", ["LFPG"] = "Paris", ["LFPO"] = "Paris Orly", ["EHAM"] = "Amsterdam", ["LEPA"] = "Palma",
        ["LEMH"] = "Menorca", ["LEIB"] = "Ibiza", ["GCTS"] = "Tenerife", ["GCXO"] = "Tenerife", ["GCLP"] = "Gran Canaria",
        ["GCRR"] = "Lanzarote", ["GCFV"] = "Fuerteventura", ["LPFR"] = "Faro", ["LPMA"] = "Madeira", ["LIRF"] = "Rome",
        ["LIPZ"] = "Venice", ["LGAV"] = "Athens", ["LGIR"] = "Heraklion", ["LGRP"] = "Rhodes", ["LGKR"] = "Corfu",
        ["LCLK"] = "Larnaca", ["LCPH"] = "Paphos", ["LMML"] = "Malta", ["EDDM"] = "Munich", ["EDDF"] = "Frankfurt",
        ["LOWW"] = "Vienna", ["LSZH"] = "Zurich", ["LSGG"] = "Geneva", ["EKCH"] = "Copenhagen", ["ENGM"] = "Oslo",
        ["ESSA"] = "Stockholm", ["EFHK"] = "Helsinki", ["BIKF"] = "Reykjavik", ["OMDB"] = "Dubai", ["OTHH"] = "Doha",
        ["KJFK"] = "New York", ["KEWR"] = "New York", ["KMCO"] = "Orlando", ["KLAS"] = "Las Vegas", ["CYYZ"] = "Toronto",
        ["VABB"] = "Mumbai", ["VIDP"] = "Delhi", ["VOBL"] = "Bengaluru", ["VOMM"] = "Chennai", ["WSSS"] = "Singapore",
        ["ZBAA"] = "Beijing", ["ZSPD"] = "Shanghai", ["VHHH"] = "Hong Kong", ["RJTT"] = "Tokyo", ["RKSI"] = "Seoul",
    };
}

public sealed class KnownRoute
{
    public string Callsign { get; set; } = "";
    public string? FlightIata { get; set; }
    public string? AirlineIcao { get; set; }
    public string? AirlineName { get; set; }
    public string? OriginIcao { get; set; }
    public string? OriginIata { get; set; }
    public string? OriginCity { get; set; }
    public string? OriginCountry { get; set; }
    public double? OriginLat { get; set; }
    public double? OriginLon { get; set; }
    public string? DestinationIcao { get; set; }
}
