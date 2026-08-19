using FlightBoard.Core.Geo;

namespace FlightBoard.Tests;

public class CpaTests
{
    [Fact]
    public void Head_on_gives_zero_miss_and_positive_time()
    {
        // Aircraft 3000 m due east of home, flying due west (track 270) at 77 m/s.
        var (ve, vn) = LocalPlane.Velocity(150, 270);
        var r = Cpa.Compute(relEast: -3000, relNorth: 0, velEast: ve, velNorth: vn);
        Assert.True(r.IsMoving);
        Assert.InRange(r.TimeToCpaSeconds, 38, 40);       // 3000 / 77.2 ≈ 38.9
        Assert.InRange(r.CpaDistanceMetres, 0, 0.01);
        Assert.InRange(r.CurrentDistanceMetres, 2999, 3001);
    }

    [Fact]
    public void Offset_track_gives_lateral_miss()
    {
        // Same as above but the aircraft is 500 m north of the extended centreline.
        var (ve, vn) = LocalPlane.Velocity(150, 270);
        var r = Cpa.Compute(relEast: -3000, relNorth: -500, velEast: ve, velNorth: vn);
        Assert.InRange(r.CpaDistanceMetres, 499, 501);
        Assert.InRange(r.TimeToCpaSeconds, 38, 40);
    }

    [Fact]
    public void Already_past_gives_negative_time()
    {
        var (ve, vn) = LocalPlane.Velocity(150, 270);
        var r = Cpa.Compute(relEast: 2000, relNorth: 0, velEast: ve, velNorth: vn); // home is east of the aircraft, it flies west
        Assert.True(r.TimeToCpaSeconds < 0);
    }

    [Fact]
    public void Stationary_is_not_moving()
    {
        var r = Cpa.Compute(1000, 1000, 0, 0);
        Assert.False(r.IsMoving);
        Assert.True(double.IsPositiveInfinity(r.TimeToCpaSeconds));
    }

    [Fact]
    public void LocalPlane_round_trips()
    {
        var plane = new LocalPlane(new GeoPoint(51.171, -0.051));
        var p = plane.FromLocal(1234, -567);
        var (e, n) = plane.ToLocal(p);
        Assert.InRange(e, 1233.9, 1234.1);
        Assert.InRange(n, -567.1, -566.9);
    }

    [Fact]
    public void Velocity_north_and_east()
    {
        var (e, n) = LocalPlane.Velocity(100, 0);
        Assert.InRange(e, -0.001, 0.001);
        Assert.InRange(n, 51.4, 51.5);
        (e, n) = LocalPlane.Velocity(100, 90);
        Assert.InRange(e, 51.4, 51.5);
        Assert.InRange(n, -0.001, 0.001);
    }
}
