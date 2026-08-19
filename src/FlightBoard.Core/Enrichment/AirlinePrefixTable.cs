namespace FlightBoard.Core.Enrichment;

/// <summary>
/// Offline ICAO-prefix → airline name fallback, used when the route DBs know nothing about a callsign.
/// Biased towards who actually flies into Gatwick, plus the obvious majors and military users.
/// </summary>
public static class AirlinePrefixTable
{
    public sealed record Airline(string Icao, string Iata, string Name);

    private static readonly Dictionary<string, Airline> ByIcao = new[]
    {
        new Airline("EZY", "U2", "easyJet"), new Airline("EJU", "EC", "easyJet Europe"), new Airline("EZS", "DS", "easyJet Switzerland"),
        new Airline("BAW", "BA", "British Airways"), new Airline("SHT", "BA", "BA Shuttle"), new Airline("EFW", "BA", "BA Euroflyer"), new Airline("CFE", "BA", "BA CityFlyer"),
        new Airline("TOM", "BY", "TUI Airways"), new Airline("TUI", "X3", "TUIfly"),
        new Airline("WUK", "W9", "Wizz Air UK"), new Airline("WZZ", "W6", "Wizz Air"), new Airline("WMT", "W4", "Wizz Air Malta"),
        new Airline("VLG", "VY", "Vueling"), new Airline("RYR", "FR", "Ryanair"), new Airline("RUK", "RK", "Ryanair UK"), new Airline("MAY", "MY", "Malta Air"),
        new Airline("NOZ", "DY", "Norwegian"), new Airline("NAX", "DY", "Norwegian"), new Airline("NSZ", "D8", "Norwegian Sweden"),
        new Airline("AUR", "FM", "Aurigny"), new Airline("BEE", "BE", "Flybe"), new Airline("LOG", "LM", "Loganair"), new Airline("EXS", "LS", "Jet2"),
        new Airline("VIR", "VS", "Virgin Atlantic"), new Airline("UAE", "EK", "Emirates"), new Airline("QTR", "QR", "Qatar Airways"), new Airline("ETD", "EY", "Etihad"),
        new Airline("THY", "TK", "Turkish Airlines"), new Airline("PGT", "PC", "Pegasus"), new Airline("SXS", "XQ", "SunExpress"),
        new Airline("AFR", "AF", "Air France"), new Airline("KLM", "KL", "KLM"), new Airline("DLH", "LH", "Lufthansa"), new Airline("EWG", "EW", "Eurowings"),
        new Airline("AUA", "OS", "Austrian"), new Airline("SWR", "LX", "Swiss"), new Airline("BEL", "SN", "Brussels Airlines"), new Airline("SAS", "SK", "SAS"),
        new Airline("IBE", "IB", "Iberia"), new Airline("IBS", "I2", "Iberia Express"), new Airline("AEA", "UX", "Air Europa"), new Airline("TAP", "TP", "TAP Air Portugal"),
        new Airline("AZA", "AZ", "ITA Airways"), new Airline("ITY", "AZ", "ITA Airways"), new Airline("EIN", "EI", "Aer Lingus"), new Airline("EUK", "EI", "Aer Lingus UK"),
        new Airline("ICE", "FI", "Icelandair"), new Airline("FIN", "AY", "Finnair"), new Airline("LOT", "LO", "LOT Polish"), new Airline("CSA", "OK", "Czech Airlines"),
        new Airline("AEE", "A3", "Aegean"), new Airline("OAL", "OA", "Olympic Air"), new Airline("CYP", "CY", "Cyprus Airways"), new Airline("TRA", "HV", "Transavia"),
        new Airline("ELY", "LY", "El Al"), new Airline("RJA", "RJ", "Royal Jordanian"), new Airline("MSR", "MS", "EgyptAir"), new Airline("RAM", "AT", "Royal Air Maroc"),
        new Airline("TAR", "TU", "Tunisair"), new Airline("DAH", "AH", "Air Algerie"), new Airline("ETH", "ET", "Ethiopian"), new Airline("KQA", "KQ", "Kenya Airways"),
        new Airline("RWD", "WB", "RwandAir"), new Airline("GLO", "G3", "Gol"), new Airline("AVA", "AV", "Avianca"), new Airline("AAL", "AA", "American Airlines"),
        new Airline("DAL", "DL", "Delta"), new Airline("UAL", "UA", "United"), new Airline("JBU", "B6", "JetBlue"), new Airline("WJA", "WS", "WestJet"), new Airline("ACA", "AC", "Air Canada"),
        new Airline("ROU", "RV", "Air Canada Rouge"), new Airline("ATN", "8C", "Air Transat"), new Airline("CCA", "CA", "Air China"), new Airline("CES", "MU", "China Eastern"),
        new Airline("CSN", "CZ", "China Southern"), new Airline("CHH", "HU", "Hainan"), new Airline("CPA", "CX", "Cathay Pacific"), new Airline("SIA", "SQ", "Singapore Airlines"),
        new Airline("KAL", "KE", "Korean Air"), new Airline("JAL", "JL", "Japan Airlines"), new Airline("ANA", "NH", "ANA"), new Airline("AIC", "AI", "Air India"),
        new Airline("VTI", "UK", "Vistara"), new Airline("SVA", "SV", "Saudia"), new Airline("KAC", "KU", "Kuwait Airways"), new Airline("GFA", "GF", "Gulf Air"),
        new Airline("OMA", "WY", "Oman Air"), new Airline("UZB", "HY", "Uzbekistan Airways"), new Airline("AHY", "J2", "Azerbaijan Airlines"), new Airline("PIA", "PK", "PIA"),
        new Airline("NPT", "NO", "Neos"), new Airline("CFG", "DE", "Condor"), new Airline("EDW", "WK", "Edelweiss"), new Airline("AMC", "KM", "Air Malta"), new Airline("KMM", "KM", "KM Malta Airlines"),
        new Airline("CAI", "XC", "Corendon"), new Airline("CND", "CD", "Corendon Dutch"), new Airline("FHY", "FH", "Freebird"), new Airline("ISR", "6H", "Israir"),
        new Airline("EVE", "E9", "Evelop"), new Airline("IBK", "CD", "Iberojet"), new Airline("WWI", "W2", "Flexflight"), new Airline("TVF", "TO", "Transavia France"),
        new Airline("FBU", "BF", "French Bee"), new Airline("ASL", "", "ASL Airlines"), new Airline("DHK", "D0", "DHL Air UK"), new Airline("BCS", "QY", "European Air Transport"),
        new Airline("TAY", "3V", "ASL Belgium"), new Airline("FDX", "FX", "FedEx"), new Airline("UPS", "5X", "UPS"), new Airline("GTI", "5Y", "Atlas Air"), new Airline("CLX", "CV", "Cargolux"),
        new Airline("NJE", "1I", "NetJets Europe"), new Airline("VJT", "VJ", "VistaJet"), new Airline("GMA", "", "Gama Aviation"), new Airline("LXJ", "", "Flexjet"),
        new Airline("RRR", "", "Royal Air Force"), new Airline("RFR", "", "Royal Air Force"), new Airline("KRF", "", "RAF 32 Squadron"), new Airline("NVY", "", "Royal Navy"),
        new Airline("AAC", "", "Army Air Corps"), new Airline("CFC", "", "Canadian Forces"), new Airline("GAF", "", "German Air Force"), new Airline("FAF", "", "French Air Force"),
        new Airline("CTM", "", "French Air Force"), new Airline("IAM", "", "Italian Air Force"), new Airline("BAF", "", "Belgian Air Force"), new Airline("NAF", "", "Netherlands Air Force"),
        new Airline("NOW", "", "Norwegian Air Force"), new Airline("DAF", "", "Danish Air Force"), new Airline("SVF", "", "Swedish Air Force"), new Airline("NATO", "", "NATO"),
        new Airline("RCH", "", "US Air Force"), new Airline("DUKE", "", "US Army"), new Airline("CNV", "", "US Navy"), new Airline("SPAR", "", "US Air Force"),
        new Airline("ASY", "", "Royal Australian Air Force"), new Airline("HKY", "", "Hungarian Air Force"),
    }.ToDictionary(a => a.Icao, StringComparer.OrdinalIgnoreCase);

    public static Airline? Lookup(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign) || callsign.Length < 3) return null;
        var prefix = callsign[..3].ToUpperInvariant();
        if (!prefix.All(char.IsLetter)) return null;
        return ByIcao.GetValueOrDefault(prefix);
    }

    /// <summary>Converts "EZY8123" to "U28123" when we know the airline's IATA code; otherwise returns the callsign.</summary>
    public static string ToIataFlight(string callsign, string? iata)
    {
        if (string.IsNullOrWhiteSpace(iata) || callsign.Length < 4) return callsign;
        var suffix = callsign[3..];
        return suffix.Length > 0 && char.IsDigit(suffix[0]) ? iata + suffix : callsign;
    }
}
