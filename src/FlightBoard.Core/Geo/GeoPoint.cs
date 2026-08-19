namespace FlightBoard.Core.Geo;

public readonly record struct GeoPoint(double Lat, double Lon)
{
    public static double HaversineKm(GeoPoint a, GeoPoint b)
    {
        const double R = 6371.0;
        var dLat = Deg2Rad(b.Lat - a.Lat);
        var dLon = Deg2Rad(b.Lon - a.Lon);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Deg2Rad(a.Lat)) * Math.Cos(Deg2Rad(b.Lat)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(h));
    }

    public static double Deg2Rad(double d) => d * Math.PI / 180.0;
    public static double Rad2Deg(double r) => r * 180.0 / Math.PI;
}
