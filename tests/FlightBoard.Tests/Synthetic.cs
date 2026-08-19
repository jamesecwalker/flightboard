using FlightBoard.Core.Geo;
using FlightBoard.Core.Model;
using FlightBoard.Core.Tracking;

namespace FlightBoard.Tests;

/// <summary>Builds deterministic aircraft tracks for tracker tests.</summary>
public static class Synthetic
{
    public static readonly GeoPoint Home = new(51.171, -0.051);
    public static readonly DateTimeOffset T0 = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    public sealed class Plane
    {
        public string Hex = "abc123";
        public string? Callsign = "EZY8123";
        public string? Type = "A320";
        public double TrackDeg = 258;
        public double GroundSpeedKt = 150;
        public double StartDistanceM = 8000;      // metres before the house along the track
        public double LateralOffsetM = 0;
        public int AltOverHouseFt = 2200;
        public int BaroRateFpm = -750;
        public string? Squawk = "5541";
        public string Category = "A3";
        public int DbFlags = 0;
        /// <summary>Seconds after T0 this aircraft starts its run.</summary>
        public double StartOffsetSeconds = 0;
        /// <summary>Optional per-sample override (elapsed seconds on this plane's track, sample) → sample.</summary>
        public Func<double, AircraftSample, AircraftSample?>? Mutate;
    }

    public static AircraftSample? SampleAt(Plane p, double elapsedSeconds, LocalPlane plane)
    {
        if (elapsedSeconds < 0) return null;
        var (ve, vn) = LocalPlane.Velocity(p.GroundSpeedKt, p.TrackDeg);
        var speed = Math.Sqrt(ve * ve + vn * vn);
        var (ue, un) = (ve / speed, vn / speed);
        var (re, rn) = (un, -ue);
        var along = -p.StartDistanceM + speed * elapsedSeconds;
        if (along > 10000) return null;
        var pos = plane.FromLocal(ue * along + re * p.LateralOffsetM, un * along + rn * p.LateralOffsetM);
        var alt = p.AltOverHouseFt - along / 1000.0 * 172;
        var s = new AircraftSample(p.Hex, p.Callsign, null, p.Type, null, pos.Lat, pos.Lon, (int)alt, false,
            p.GroundSpeedKt, p.TrackDeg, p.BaroRateFpm, p.Squawk, "none", p.Category, p.DbFlags, 0.2, 0.1);
        return p.Mutate is null ? s : p.Mutate(elapsedSeconds, s);
    }

    /// <summary>Polls every <paramref name="stepSeconds"/> for <paramref name="durationSeconds"/>.</summary>
    public static IEnumerable<SourcePoll> Polls(IEnumerable<Plane> planes, double durationSeconds, double stepSeconds = 2)
    {
        var lp = new LocalPlane(Home);
        var list = planes.ToList();
        for (double t = 0; t <= durationSeconds; t += stepSeconds)
        {
            var samples = new List<AircraftSample>();
            foreach (var p in list)
            {
                var s = SampleAt(p, t - p.StartOffsetSeconds, lp);
                if (s is not null) samples.Add(s);
            }
            yield return new SourcePoll(T0.AddSeconds(t), samples);
        }
    }

    public static TrackerOptions Options() => new() { Approach = { Headings = [258, 78] } };
}
