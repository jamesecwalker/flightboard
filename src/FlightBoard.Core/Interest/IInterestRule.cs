using FlightBoard.Core.Enrichment;
using FlightBoard.Core.Geo;
using FlightBoard.Core.Storage;
using FlightBoard.Core.Tracking;

namespace FlightBoard.Core.Interest;

public sealed record InterestContext(
    TrackedFlight Flight,
    Enriched Enriched,
    ISightings Sightings,
    DateTimeOffset Now,
    GeoPoint Home,
    InterestOptions Options)
{
    /// <summary>Best-known ICAO type code: ADS-B first (it is live), then the airframe DB.</summary>
    public string? TypeIcao => Enriched.FirstNonEmpty(Flight.Type, Enriched.TypeIcao)?.ToUpperInvariant();
    public string? Callsign => Flight.Callsign ?? Enriched.Callsign;
    public string? Registration => Enriched.FirstNonEmpty(Flight.Registration, Enriched.Registration);
}

/// <summary>One reason a flight might deserve the highlight. Return null when it does not apply.</summary>
public interface IInterestRule
{
    string Name { get; }
    IEnumerable<InterestTag> Evaluate(InterestContext ctx);
}

public sealed class InterestOptions
{
    /// <summary>Do not raise FIRST SEEN / NEW AIRLINE etc. until the sightings DB has had this long to fill up.</summary>
    public int FirstSightingWarmupDays { get; set; } = 7;

    /// <summary>ICAO type code → label shown on the board.</summary>
    public Dictionary<string, string> UnusualTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A388"] = "A380", ["B744"] = "747", ["B748"] = "747-8", ["B74S"] = "747SP", ["A124"] = "ANTONOV 124", ["A225"] = "ANTONOV 225",
        ["C17"] = "C-17 GLOBEMASTER", ["A400"] = "A400M ATLAS", ["C130"] = "HERCULES", ["C30J"] = "HERCULES", ["L382"] = "HERCULES",
        ["B52"] = "B-52", ["K35R"] = "KC-135", ["E3CF"] = "AWACS", ["E3TF"] = "AWACS", ["P8"] = "P-8 POSEIDON", ["VULC"] = "VULCAN",
        ["CONC"] = "CONCORDE", ["A3ST"] = "BELUGA", ["A337"] = "BELUGA XL", ["B703"] = "707", ["DC10"] = "DC-10", ["MD11"] = "MD-11",
        ["B762"] = "767", ["A306"] = "A300", ["A310"] = "A310", ["IL76"] = "IL-76", ["AN12"] = "AN-12", ["AN26"] = "AN-26",
        ["SPIT"] = "SPITFIRE", ["LANC"] = "LANCASTER", ["DC3"] = "DAKOTA", ["HURI"] = "HURRICANE",
    };
    /// <summary>ADS-B emitter categories that are always worth a look: A7 rotorcraft, B1 glider, B2 balloon, B6 UAV, B4 ultralight.</summary>
    public Dictionary<string, string> UnusualCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A7"] = "HELICOPTER", ["B1"] = "GLIDER", ["B2"] = "BALLOON", ["B4"] = "MICROLIGHT", ["B6"] = "DRONE",
    };

    /// <summary>Callsign prefixes (3-4 letters) for military / state flights → label.</summary>
    public Dictionary<string, string> MilitaryCallsignPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RRR"] = "RAF", ["RFR"] = "RAF", ["KRF"] = "RAF ROYAL FLT", ["NVY"] = "ROYAL NAVY", ["AAC"] = "ARMY AIR CORPS", ["ASCOT"] = "RAF",
        ["CFC"] = "CANADIAN FORCES", ["GAF"] = "GERMAN AF", ["FAF"] = "FRENCH AF", ["CTM"] = "FRENCH AF", ["IAM"] = "ITALIAN AF",
        ["BAF"] = "BELGIAN AF", ["NAF"] = "DUTCH AF", ["NOW"] = "NORWEGIAN AF", ["DAF"] = "DANISH AF", ["SVF"] = "SWEDISH AF", ["HKY"] = "HUNGARIAN AF",
        ["NATO"] = "NATO", ["RCH"] = "USAF", ["SPAR"] = "USAF VIP", ["SAM"] = "USAF VIP", ["DUKE"] = "US ARMY", ["CNV"] = "US NAVY", ["EVAC"] = "USAF MEDEVAC",
        ["ASY"] = "RAAF", ["IFC"] = "INDIAN AF", ["PLF"] = "POLISH AF", ["CEF"] = "CZECH AF", ["SUI"] = "SWISS AF", ["AME"] = "SPANISH AF", ["PAF"] = "PORTUGUESE AF",
        ["TUAF"] = "TURKISH AF", ["HAF"] = "HELLENIC AF", ["IAF"] = "ISRAELI AF", ["RSF"] = "SAUDI AF", ["UAF"] = "UAE AF", ["QAF"] = "QATAR AF",
    };
    /// <summary>Mode-S hex ranges allocated to military users (inclusive, hex strings).</summary>
    public List<HexRange> MilitaryHexRanges { get; set; } =
    [
        new() { From = "43C000", To = "43CFFF", Label = "UK MILITARY" },
        new() { From = "AE0000", To = "AFFFFF", Label = "US MILITARY" },
        new() { From = "3F4000", To = "3FBFFF", Label = "GERMAN MILITARY" },
        new() { From = "3A8000", To = "3AFFFF", Label = "FRENCH MILITARY" },
        new() { From = "33FF00", To = "33FFFF", Label = "ITALIAN MILITARY" },
        new() { From = "C20000", To = "C3FFFF", Label = "CANADIAN MILITARY" },
    ];
    /// <summary>UK military serials show up as the registration: ZZ###, ZM###, ZE###, ZH### etc.</summary>
    public List<string> MilitaryRegistrationPrefixes { get; set; } = ["ZZ", "ZM", "ZE", "ZH", "ZJ", "ZK", "ZA", "ZB", "ZD", "ZF", "ZG", "ZR", "XX", "XZ"];
    /// <summary>Registrations you personally care about → label. e.g. "G-XLEB": "ROYAL" or a mate's plane.</summary>
    public Dictionary<string, string> WatchRegistrations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Hexes you personally care about → label.</summary>
    public Dictionary<string, string> WatchHexes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Business-jet ICAO type prefixes (matched with StartsWith).</summary>
    public List<string> BizjetTypePrefixes { get; set; } =
    [
        "GLF", "GL5T", "GL7T", "GLEX", "GA5C", "GA6C", "GA7C", "CL30", "CL35", "CL60", "CL64", "C25", "C500", "C510", "C525", "C550", "C560", "C56X", "C650", "C680", "C68A", "C700", "C750",
        "E35L", "E50P", "E545", "E550", "E55P", "F2TH", "F900", "FA10", "FA20", "FA50", "FA6X", "FA7X", "FA8X", "H25B", "H25C", "HDJT", "HA4T", "LJ", "PC12", "PC24", "PRM1", "BE40", "ASTR", "G150", "G280",
    ];

    /// <summary>Origins further than this (km) from home count as LONG HAUL.</summary>
    public double LongHaulKm { get; set; } = 8000;
    /// <summary>Our airport: flights routed elsewhere but on our approach are diversions.</summary>
    public string HomeAirportIcao { get; set; } = "EGKK";
    /// <summary>
    /// Off by default: the route DBs only hold one leg of multi-stop flights (e.g. BA2158 Grenada-St Lucia-Gatwick shows
    /// destination St Lucia), which makes "DIVERTED" fire on perfectly normal arrivals.
    /// </summary>
    public bool DetectDiversions { get; set; } = false;
}

public sealed class HexRange
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Label { get; set; } = "MILITARY";

    public bool Contains(string hex)
    {
        if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var h)) return false;
        if (!int.TryParse(From, System.Globalization.NumberStyles.HexNumber, null, out var lo)) return false;
        if (!int.TryParse(To, System.Globalization.NumberStyles.HexNumber, null, out var hi)) return false;
        return h >= lo && h <= hi;
    }
}
