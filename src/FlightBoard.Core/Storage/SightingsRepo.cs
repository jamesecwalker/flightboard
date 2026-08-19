using Dapper;

namespace FlightBoard.Core.Storage;

public sealed record Sighting(
    long Id, string Hex, string? Callsign, string? Flight, string? Registration, string? Type,
    string? AirlineIcao, string? AirlineName, string? OriginIcao, string? OriginName, string? Tags,
    int? AltFt, DateTimeOffset SeenAt, bool IsDeparture = false);

/// <summary>What the interest rules need to know about history. Kept in memory for speed; SQLite is the durable copy.</summary>
public interface ISightings
{
    bool HasSeenHex(string hex);
    bool HasSeenAirline(string airlineIcao);
    bool HasSeenOrigin(string originIcao);
    bool HasSeenType(string type);
    int CountToday(DateTimeOffset now);
    DateTimeOffset FirstRunAt { get; }
}

public sealed class SightingsRepo : ISightings
{
    private readonly Db _db;
    private readonly HashSet<string> _hexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _airlines = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _origins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private DateOnly _todayKey;
    private int _todayCount;

    public SightingsRepo(Db db)
    {
        _db = db;
        FirstRunAt = db.FirstRunAt;
        using var c = db.Open();
        foreach (var r in c.Query<(string? Hex, string? AirlineIcao, string? OriginIcao, string? Type)>(
                     "SELECT Hex, AirlineIcao, OriginIcao, Type FROM Sightings"))
        {
            if (r.Hex is not null) _hexes.Add(r.Hex);
            if (r.AirlineIcao is not null) _airlines.Add(r.AirlineIcao);
            if (r.OriginIcao is not null) _origins.Add(r.OriginIcao);
            if (r.Type is not null) _types.Add(r.Type);
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _todayKey = today;
        _todayCount = c.ExecuteScalar<int>("SELECT COUNT(*) FROM Sightings WHERE SeenAt >= @from",
            new { from = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("O") });
    }

    public DateTimeOffset FirstRunAt { get; }
    public bool HasSeenHex(string hex) { lock (_gate) return _hexes.Contains(hex); }
    public bool HasSeenAirline(string a) { lock (_gate) return _airlines.Contains(a); }
    public bool HasSeenOrigin(string o) { lock (_gate) return _origins.Contains(o); }
    public bool HasSeenType(string t) { lock (_gate) return _types.Contains(t); }

    public int CountToday(DateTimeOffset now)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            if (today != _todayKey) { _todayKey = today; _todayCount = 0; }
            return _todayCount;
        }
    }

    public void Record(Sighting s)
    {
        using var c = _db.Open();
        c.Execute("""
            INSERT INTO Sightings(Hex, Callsign, Flight, Registration, Type, AirlineIcao, AirlineName, OriginIcao, OriginName, Tags, AltFt, SeenAt, IsDeparture)
            VALUES (@Hex, @Callsign, @Flight, @Registration, @Type, @AirlineIcao, @AirlineName, @OriginIcao, @OriginName, @Tags, @AltFt, @SeenAt, @IsDeparture)
            """, new
        {
            s.Hex, s.Callsign, s.Flight, s.Registration, s.Type, s.AirlineIcao, s.AirlineName, s.OriginIcao, s.OriginName, s.Tags, s.AltFt,
            SeenAt = s.SeenAt.ToString("O"), IsDeparture = s.IsDeparture ? 1 : 0,
        });
        lock (_gate)
        {
            _hexes.Add(s.Hex);
            if (s.AirlineIcao is not null) _airlines.Add(s.AirlineIcao);
            if (s.OriginIcao is not null) _origins.Add(s.OriginIcao);
            if (s.Type is not null) _types.Add(s.Type);
            var today = DateOnly.FromDateTime(s.SeenAt.UtcDateTime);
            if (today != _todayKey) { _todayKey = today; _todayCount = 0; }
            _todayCount++;
        }
    }

    public IReadOnlyList<Sighting> Recent(int limit = 50)
    {
        using var c = _db.Open();
        return c.Query<SightingRow>("SELECT * FROM Sightings ORDER BY Id DESC LIMIT @limit", new { limit })
            .Select(r => r.ToSighting()).ToList();
    }

    public Sighting? Get(long id)
    {
        using var c = _db.Open();
        return c.QuerySingleOrDefault<SightingRow>("SELECT * FROM Sightings WHERE Id = @id", new { id })?.ToSighting();
    }

    private sealed class SightingRow
    {
        public long Id { get; set; }
        public string Hex { get; set; } = "";
        public string? Callsign { get; set; }
        public string? Flight { get; set; }
        public string? Registration { get; set; }
        public string? Type { get; set; }
        public string? AirlineIcao { get; set; }
        public string? AirlineName { get; set; }
        public string? OriginIcao { get; set; }
        public string? OriginName { get; set; }
        public string? Tags { get; set; }
        public long? AltFt { get; set; }
        public string SeenAt { get; set; } = "";
        public long IsDeparture { get; set; }
        public Sighting ToSighting() => new(Id, Hex, Callsign, Flight, Registration, Type, AirlineIcao, AirlineName, OriginIcao, OriginName, Tags,
            AltFt is null ? null : (int)AltFt, DateTimeOffset.Parse(SeenAt, null, System.Globalization.DateTimeStyles.RoundtripKind), IsDeparture != 0);
    }
}
