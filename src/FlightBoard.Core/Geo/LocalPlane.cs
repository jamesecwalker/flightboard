namespace FlightBoard.Core.Geo;

/// <summary>
/// Flat east/north metres around a home point. Equirectangular is accurate to well under 1%
/// out to the ~50 km we care about, and it makes the CPA maths trivial.
/// </summary>
public sealed class LocalPlane
{
    private const double MetresPerDegLat = 111_320.0;
    private readonly double _metresPerDegLon;

    public LocalPlane(GeoPoint origin)
    {
        Origin = origin;
        _metresPerDegLon = MetresPerDegLat * Math.Cos(GeoPoint.Deg2Rad(origin.Lat));
    }

    public GeoPoint Origin { get; }

    public (double East, double North) ToLocal(GeoPoint p) =>
        ((p.Lon - Origin.Lon) * _metresPerDegLon, (p.Lat - Origin.Lat) * MetresPerDegLat);

    public GeoPoint FromLocal(double east, double north) =>
        new(Origin.Lat + north / MetresPerDegLat, Origin.Lon + east / _metresPerDegLon);

    /// <summary>Velocity vector in m/s from ground speed (knots) and true track (degrees, 0 = north, clockwise).</summary>
    public static (double East, double North) Velocity(double groundSpeedKt, double trackDeg)
    {
        var ms = groundSpeedKt * 0.514444;
        var rad = GeoPoint.Deg2Rad(trackDeg);
        return (ms * Math.Sin(rad), ms * Math.Cos(rad));
    }
}
