namespace FlightBoard.Core.Geo;

public readonly record struct CpaResult(
    /// <summary>Seconds until closest approach; negative means it has already happened.</summary>
    double TimeToCpaSeconds,
    /// <summary>Lateral miss distance at closest approach, metres.</summary>
    double CpaDistanceMetres,
    /// <summary>Distance right now, metres.</summary>
    double CurrentDistanceMetres,
    bool IsMoving);

/// <summary>Straight-line closest-point-of-approach of a moving aircraft to a fixed point.</summary>
public static class Cpa
{
    /// <param name="relEast">home.east − aircraft.east (metres)</param>
    /// <param name="relNorth">home.north − aircraft.north (metres)</param>
    /// <param name="velEast">aircraft velocity east (m/s)</param>
    /// <param name="velNorth">aircraft velocity north (m/s)</param>
    public static CpaResult Compute(double relEast, double relNorth, double velEast, double velNorth)
    {
        var dNow = Math.Sqrt(relEast * relEast + relNorth * relNorth);
        var v2 = velEast * velEast + velNorth * velNorth;
        if (v2 < 1e-6) return new CpaResult(double.PositiveInfinity, dNow, dNow, false);

        var t = (relEast * velEast + relNorth * velNorth) / v2;
        var missE = relEast - velEast * t;
        var missN = relNorth - velNorth * t;
        var dCpa = Math.Sqrt(missE * missE + missN * missN);
        return new CpaResult(t, dCpa, dNow, true);
    }
}
