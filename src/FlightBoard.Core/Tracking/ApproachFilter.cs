using FlightBoard.Core.Model;

namespace FlightBoard.Core.Tracking;

public static class ApproachFilter
{
    /// <summary>Returns null when the sample passes, otherwise a short reason (useful for tuning logs).</summary>
    public static string? Reject(AircraftSample s, ApproachFilterOptions o)
    {
        if (!o.Enabled) return null;
        if (s.OnGround) return "on-ground";
        if (o.MaxAltitudeFt is { } maxAlt && s.AltBaroFt is { } alt && alt > maxAlt) return $"alt {alt}>{maxAlt}";
        if (o.MaxClimbFpm is { } maxClimb && s.BaroRateFpm is { } rate && rate > maxClimb) return $"climbing {rate}fpm";
        if (o.Headings.Count > 0 && s.TrackDeg is { } trk)
        {
            var ok = o.Headings.Any(h => AngleDiff(h, trk) <= o.HeadingToleranceDeg);
            if (!ok) return $"track {trk:0}";
        }
        return null;
    }

    public static double AngleDiff(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }
}
