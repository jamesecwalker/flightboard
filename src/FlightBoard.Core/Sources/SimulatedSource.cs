using FlightBoard.Core.Geo;
using FlightBoard.Core.Model;

namespace FlightBoard.Core.Sources;

/// <summary>
/// Flies pretend aircraft down the approach over the house so the whole pipeline (tracker,
/// enrichment, rules, board) can be exercised at the desk with no traffic overhead.
/// Every Nth aircraft is something interesting so the highlight path gets a workout too.
/// </summary>
public sealed class SimulatedSource : IAircraftSource
{
    private sealed record Profile(string Hex, string? Callsign, string? Reg, string? Type, string? Desc,
        string Category, string Squawk, string Emergency, int DbFlags);

    private static readonly Profile[] Ordinary =
    [
        new("406a3b", "EZY8123", "G-EZTD", "A320", "AIRBUS A-320", "A3", "5541", "none", 0),
        new("4008f1", "BAW2723", "G-EUYA", "A320", "AIRBUS A-320", "A3", "1234", "none", 0),
        new("407126", "TOM4536", "G-TUIC", "B738", "BOEING 737-800", "A3", "6231", "none", 0),
        new("40792a", "WUK2831", "G-WUKF", "A21N", "AIRBUS A-321neo", "A3", "4471", "none", 0),
        new("3443c7", "VLG8776", "EC-MFN", "A320", "AIRBUS A-320", "A3", "7031", "none", 0),
        new("4ca9c2", "RYR3PW", "EI-EBM", "B738", "BOEING 737-800", "A3", "2205", "none", 0),
        new("478722", "NOZ1305", "LN-NGT", "B738", "BOEING 737-800", "A3", "3320", "none", 0),
        new("40663d", "EZY6031", "G-EZWO", "A320", "AIRBUS A-320", "A3", "5522", "none", 0),
        new("896427", "UAE15", "A6-EUB", "A388", "AIRBUS A-380-800", "A5", "1041", "none", 0),
        new("400b1c", "TOM52", "G-TUIH", "B789", "BOEING 787-9", "A5", "4462", "none", 0),
    ];

    private static readonly Profile[] Interesting =
    [
        new("43c6e2", "RRR4321", "ZM402", "A400", "AIRBUS A-400M ATLAS", "A5", "3711", "none", 1),
        new("896427", "UAE15", "A6-EUB", "A388", "AIRBUS A-380-800", "A5", "1041", "none", 0),
        new("4b1a11", null, "M-EGGA", "GLF6", "GULFSTREAM G650", "A2", "6544", "none", 0),
        new("40792a", "WUK2831", "G-WUKF", "A21N", "AIRBUS A-321neo", "A3", "7700", "general", 0),
        new("3c4b2c", "GAF102", "10+03", "A319", "AIRBUS A-319", "A3", "0451", "none", 1),
    ];

    private sealed record Active(Profile Profile, DateTimeOffset SpawnAt);

    private readonly SimulatorOptions _o;
    private readonly LocalPlane _plane;
    private readonly TimeProvider _time;
    private readonly List<Active> _active = new();
    private readonly DateTimeOffset _startedAt;
    private int _spawned;
    private DateTimeOffset _nextSpawn;

    public SimulatedSource(SimulatorOptions options, GeoPoint home, TimeProvider? time = null)
    {
        _o = options;
        _plane = new LocalPlane(home);
        _time = time ?? TimeProvider.System;
        _startedAt = _time.GetUtcNow();
        _nextSpawn = _startedAt.AddSeconds(_o.FirstAfterSeconds);
    }

    public string Name => "simulated";

    public Task<SourcePoll> PollAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        while (now >= _nextSpawn)
        {
            _spawned++;
            var interesting = _o.InterestingEveryN > 0 && _spawned % _o.InterestingEveryN == 0;
            var pool = interesting ? Interesting : Ordinary;
            var profile = pool[(_spawned / (interesting ? _o.InterestingEveryN : 1)) % pool.Length];
            _active.Add(new Active(profile, _nextSpawn));
            _nextSpawn = _nextSpawn.AddSeconds(_o.IntervalSeconds);
        }

        var samples = new List<AircraftSample>();
        foreach (var a in _active.ToList())
        {
            var s = Sample(a, now);
            if (s is null) { _active.Remove(a); continue; }
            samples.Add(s);
        }
        return Task.FromResult(new SourcePoll(now, samples));
    }

    private AircraftSample? Sample(Active a, DateTimeOffset now)
    {
        var elapsed = (now - a.SpawnAt).TotalSeconds;
        var (ve, vn) = LocalPlane.Velocity(_o.GroundSpeedKt, _o.TrackDeg);
        var speed = Math.Sqrt(ve * ve + vn * vn);
        var (ue, un) = (ve / speed, vn / speed);      // unit along-track
        var (re, rn) = (un, -ue);                      // unit right-of-track
        var along = -_o.SpawnDistanceKm * 1000 + speed * elapsed;   // metres along track relative to house (negative = not yet there)
        if (along > 8000) return null;                 // 8 km past the house: gone

        var east = ue * along + re * _o.LateralOffsetMetres;
        var north = un * along + rn * _o.LateralOffsetMetres;
        var pos = _plane.FromLocal(east, north);

        const double glideFtPerKm = 172;               // 3 degree glideslope
        var alt = _o.AltitudeOverHouseFt - along / 1000.0 * glideFtPerKm;
        var rate = (int)Math.Round(-speed * Math.Tan(GeoPoint.Deg2Rad(3)) * 196.85);
        if (alt < 50) return null;

        var p = a.Profile;
        return new AircraftSample(
            Hex: p.Hex, Callsign: p.Callsign, Registration: p.Reg, Type: p.Type, Description: p.Desc,
            Lat: pos.Lat, Lon: pos.Lon, AltBaroFt: (int)Math.Round(alt), OnGround: false,
            GroundSpeedKt: _o.GroundSpeedKt, TrackDeg: _o.TrackDeg, BaroRateFpm: rate,
            Squawk: p.Squawk, Emergency: p.Emergency, Category: p.Category, DbFlags: p.DbFlags,
            SeenPosSeconds: 0.3, SeenSeconds: 0.1);
    }
}
