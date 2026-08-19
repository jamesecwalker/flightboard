using FlightBoard.Core.Enrichment;
using FlightBoard.Core.Interest;
using FlightBoard.Core.Model;
using FlightBoard.Core.Tracking;

namespace FlightBoard.Core.Board;

public sealed class BoardOptions
{
    public int Rows { get; set; } = 4;
    public int Cols { get; set; } = 22;
    public string Charset { get; set; } = BoardCapabilities.DefaultCharset;
    public bool Colour { get; set; } = true;
    /// <summary>Show "U2 8123" (what is on the boarding pass) rather than the ICAO callsign "EZY8123" when we know the airline's IATA code.</summary>
    public bool PreferIataFlightNumber { get; set; } = true;
    /// <summary>First idle line.</summary>
    public string IdleTitle { get; set; } = "GATWICK ARRIVALS";
    /// <summary>Local time zone for the idle clock.</summary>
    public string TimeZone { get; set; } = "Europe/London";
    /// <summary>
    /// Gatwick departures get held level at 6-7,000 ft and can pass over the house too. Off = ignore them;
    /// on = show them as "TO BARCELONA" instead of "FROM".
    /// </summary>
    public bool ShowDepartures { get; set; } = true;

    /// <summary>ICAO type designator → what the board shows. Anything not listed shows the raw code (A320, B738...).</summary>
    public Dictionary<string, string> TypeDisplayNames { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Airbus
        ["A19N"] = "A319NEO", ["A20N"] = "A320NEO", ["A21N"] = "A321NEO", ["A332"] = "A330-200", ["A333"] = "A330-300", ["A339"] = "A330NEO",
        ["A342"] = "A340", ["A343"] = "A340", ["A345"] = "A340", ["A346"] = "A340", ["A359"] = "A350", ["A35K"] = "A350-1000", ["A388"] = "A380",
        ["A306"] = "A300", ["A310"] = "A310", ["A3ST"] = "BELUGA", ["A337"] = "BELUGA XL", ["BCS1"] = "A220-100", ["BCS3"] = "A220-300",
        // Boeing
        ["B733"] = "737-300", ["B734"] = "737-400", ["B735"] = "737-500", ["B736"] = "737-600", ["B737"] = "737-700", ["B738"] = "737-800", ["B739"] = "737-900",
        ["B37M"] = "737 MAX 7", ["B38M"] = "737 MAX 8", ["B39M"] = "737 MAX 9", ["B3XM"] = "737 MAX 10",
        ["B744"] = "747-400", ["B748"] = "747-8", ["B752"] = "757-200", ["B753"] = "757-300", ["B762"] = "767-200", ["B763"] = "767-300", ["B764"] = "767-400",
        ["B772"] = "777-200", ["B77L"] = "777-200LR", ["B773"] = "777-300", ["B77W"] = "777-300ER", ["B778"] = "777-8", ["B779"] = "777-9",
        ["B788"] = "787-8", ["B789"] = "787-9", ["B78X"] = "787-10", ["B703"] = "707", ["B721"] = "727", ["B722"] = "727",
        // Regional / turboprop
        ["E170"] = "E170", ["E175"] = "E175", ["E190"] = "E190", ["E195"] = "E195", ["E290"] = "E190-E2", ["E295"] = "E195-E2",
        ["CRJ2"] = "CRJ200", ["CRJ7"] = "CRJ700", ["CRJ9"] = "CRJ900", ["CRJX"] = "CRJ1000", ["AT43"] = "ATR 42", ["AT45"] = "ATR 42", ["AT46"] = "ATR 42",
        ["AT72"] = "ATR 72", ["AT73"] = "ATR 72", ["AT75"] = "ATR 72", ["AT76"] = "ATR 72", ["DH8A"] = "DASH 8", ["DH8B"] = "DASH 8", ["DH8C"] = "DASH 8", ["DH8D"] = "DASH 8",
        ["SF34"] = "SAAB 340", ["SB20"] = "SAAB 2000", ["D328"] = "DO 328", ["J328"] = "DO 328", ["F50"] = "FOKKER 50", ["F70"] = "FOKKER 70", ["F100"] = "FOKKER 100",
        ["DHC6"] = "TWIN OTTER", ["BN2P"] = "ISLANDER", ["TRIS"] = "TRISLANDER", ["DC3"] = "DAKOTA", ["JS41"] = "JETSTREAM", ["B190"] = "BEECH 1900",
        // Business jets
        ["GLF4"] = "G450", ["GLF5"] = "G550", ["GLF6"] = "G650", ["GA5C"] = "G500", ["GA6C"] = "G600", ["GA7C"] = "G700", ["GL5T"] = "GLOBAL 5000",
        ["GL7T"] = "GLOBAL 7500", ["GLEX"] = "GLOBAL EXP", ["CL30"] = "CHALLENGER", ["CL35"] = "CHALLENGER", ["CL60"] = "CHALLENGER", ["CL64"] = "CHALLENGER",
        ["C25A"] = "CITATION", ["C25B"] = "CITATION", ["C25C"] = "CITATION", ["C510"] = "MUSTANG", ["C525"] = "CITATION", ["C550"] = "CITATION",
        ["C560"] = "CITATION", ["C56X"] = "CITATION XLS", ["C680"] = "CITATION", ["C68A"] = "CITATION", ["C700"] = "CITATION", ["C750"] = "CITATION X",
        ["E35L"] = "LEGACY 650", ["E545"] = "PRAETOR", ["E550"] = "PRAETOR", ["E55P"] = "PHENOM 300", ["E50P"] = "PHENOM 100",
        ["F2TH"] = "FALCON 2000", ["F900"] = "FALCON 900", ["FA7X"] = "FALCON 7X", ["FA8X"] = "FALCON 8X", ["FA50"] = "FALCON 50",
        ["H25B"] = "HAWKER 800", ["H25C"] = "HAWKER 1000", ["HDJT"] = "HONDAJET", ["PC12"] = "PC-12", ["PC24"] = "PC-24", ["PRM1"] = "PREMIER", ["LJ45"] = "LEARJET", ["LJ60"] = "LEARJET", ["LJ75"] = "LEARJET",
        // Helicopters
        ["A109"] = "AW109", ["A119"] = "AW119", ["A139"] = "AW139", ["A149"] = "AW149", ["A169"] = "AW169", ["A189"] = "AW189",
        ["EC20"] = "EC120", ["EC30"] = "EC130", ["EC35"] = "H135", ["EC45"] = "H145", ["EC55"] = "EC155", ["EC75"] = "H175", ["AS50"] = "H125", ["AS55"] = "AS355", ["AS65"] = "DAUPHIN",
        ["B06"] = "JETRANGER", ["B407"] = "BELL 407", ["B429"] = "BELL 429", ["B412"] = "BELL 412", ["R22"] = "ROBINSON 22", ["R44"] = "ROBINSON 44", ["R66"] = "ROBINSON 66",
        ["S76"] = "S-76", ["S92"] = "S-92", ["H47"] = "CHINOOK", ["H60"] = "BLACK HAWK", ["H64"] = "APACHE", ["LYNX"] = "LYNX", ["WLDC"] = "WILDCAT", ["PUMA"] = "PUMA", ["EH10"] = "MERLIN",
        // Military / heavies / classics
        ["C17"] = "C-17", ["A400"] = "A400M", ["C130"] = "C-130", ["C30J"] = "C-130J", ["L382"] = "C-130", ["K35R"] = "KC-135", ["E3CF"] = "AWACS", ["E3TF"] = "AWACS",
        ["P8"] = "P-8", ["VOY"] = "VOYAGER", ["A332MRTT"] = "VOYAGER", ["TEX2"] = "TEXAN", ["HAWK"] = "HAWK", ["EUFI"] = "TYPHOON", ["F35"] = "F-35", ["F15"] = "F-15", ["F16"] = "F-16", ["TOR"] = "TORNADO",
        ["A124"] = "AN-124", ["A225"] = "AN-225", ["IL76"] = "IL-76", ["B52"] = "B-52", ["SPIT"] = "SPITFIRE", ["HURI"] = "HURRICANE", ["LANC"] = "LANCASTER", ["CONC"] = "CONCORDE",
        // GA
        ["C172"] = "CESSNA 172", ["C182"] = "CESSNA 182", ["C152"] = "CESSNA 152", ["P28A"] = "PIPER", ["P28R"] = "PIPER", ["PA28"] = "PIPER", ["PA34"] = "SENECA", ["PA46"] = "MALIBU",
        ["SR20"] = "CIRRUS", ["SR22"] = "CIRRUS", ["DA40"] = "DIAMOND", ["DA42"] = "DIAMOND", ["DA62"] = "DIAMOND", ["TBM9"] = "TBM 900", ["TBM8"] = "TBM 850", ["GLID"] = "GLIDER", ["EVOT"] = "EUROSTAR",
    };

    /// <summary>Friendly type for the board, or the raw ICAO code if unknown.</summary>
    public string? DisplayType(string? icao) =>
        string.IsNullOrWhiteSpace(icao) ? icao : TypeDisplayNames.TryGetValue(icao.Trim(), out var n) ? n : icao.Trim().ToUpperInvariant();

    public BoardCapabilities ToCapabilities() => new(Rows, Cols, Charset, Colour, TimeSpan.Zero);
}

/// <summary>Turns what the engine knows into the words that go on the board.</summary>
public static class MessageBuilder
{
    public static BoardMessage ForFlight(TrackedFlight f, Enriched e, InterestResult interest, BoardOptions o, DateTimeOffset now, bool isDeparture = false)
    {
        var callsign = f.Callsign ?? e.Callsign;
        // "U2 8123" is what people know from a boarding pass; but alphanumeric callsigns (AUR2LG, RYR3PW) have no
        // meaningful IATA form, so only swap when the numeric part is purely digits.
        var flight = o.PreferIataFlightNumber && !string.IsNullOrWhiteSpace(e.FlightIata) && HasNumericSuffix(e.FlightIata) ? e.FlightIata! : callsign;
        if (string.IsNullOrWhiteSpace(flight)) flight = Enriched.FirstNonEmpty(e.Registration, f.Registration, f.Hex.ToUpperInvariant())!;
        flight = SpaceFlightNumber(flight);

        var airline = Enriched.FirstNonEmpty(e.AirlineName, e.Owner, "");
        var origin = (isDeparture ? e.DestinationDisplay : e.OriginDisplay) ?? "";
        var type = o.DisplayType(Enriched.FirstNonEmpty(f.Type, e.TypeIcao));

        return new BoardMessage(
            Flight: flight,
            Airline: airline!,
            Origin: origin,
            Registration: Enriched.FirstNonEmpty(f.Registration, e.Registration),
            Type: type,
            Tag: interest.Best,
            ShownAt: now,
            IsDeparture: isDeparture);
    }

    public static BoardMessage Idle(BoardOptions o, DateTimeOffset now, TrackedFlight? next, int countToday)
    {
        var tz = ResolveTz(o.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, tz);
        string line3;
        if (next is not null && !double.IsNaN(next.TimeToCpaSeconds))
        {
            var mins = Math.Max(1, (int)Math.Round(next.TimeToCpaSeconds / 60.0));
            var who = string.IsNullOrWhiteSpace(next.Callsign) ? "TRAFFIC" : SpaceFlightNumber(next.Callsign);
            line3 = $"NEXT {who} IN {mins} MIN";
        }
        else line3 = countToday == 1 ? "1 PLANE TODAY" : $"{countToday} PLANES TODAY";
        return BoardMessage.Idle(now, o.IdleTitle, local.ToString("HH:mm"), line3);
    }

    private static bool HasNumericSuffix(string s)
    {
        var i = 0;
        while (i < s.Length && !char.IsDigit(s[i])) i++;
        return i > 0 && i < s.Length && s[i..].All(char.IsDigit);
    }

    /// <summary>"EZY8123" → "EZY 8123", "U28123" → "U2 8123". Leaves registrations alone.</summary>
    public static string SpaceFlightNumber(string s)
    {
        s = s.Trim();
        if (s.Contains('-') || s.Contains(' ')) return s;
        var i = 0;
        while (i < s.Length && !char.IsDigit(s[i])) i++;
        if (i is 0 or >= 4 || i >= s.Length) return s;
        // Two-char IATA codes can end in a digit (U2, W6): treat "U28123" as "U2 8123".
        if (i == 1)
            return s.Length >= 4 && s[1..].All(char.IsDigit) ? s[..2] + " " + s[2..] : s; // "N123AB" is a registration: leave it
        return s[..i] + " " + s[i..];
    }

    private static TimeZoneInfo ResolveTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Local; }
    }
}
