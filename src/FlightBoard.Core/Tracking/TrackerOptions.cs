namespace FlightBoard.Core.Tracking;

/// <summary>Everything that decides *when* the board flips. All runtime-tuneable via PUT /api/config.</summary>
public sealed class TrackerOptions
{
    /// <summary>Start flipping this many seconds before the aircraft is overhead. The sim's flip takes ~3 s, so 45 ⇒ readable ~40 s ahead.</summary>
    public double LeadSeconds { get; set; } = 45;
    /// <summary>
    /// Minimum lateral miss distance (metres) at closest approach that counts as "overhead", regardless of height.
    /// With <see cref="MinElevationDeg"/> set, the effective corridor grows with altitude (see <see cref="EffectiveCorridorMetres"/>).
    /// </summary>
    public double CorridorMetres { get; set; } = 800;
    /// <summary>
    /// An aircraft counts as overhead if, at its closest point, it will be at least this many degrees above the horizon.
    /// 20° catches the arrivals being vectored 3-4 km from the house at 4-6,000 ft that you can clearly hear and see;
    /// 30-40° means "pretty much directly above". 0 disables and only CorridorMetres applies.
    /// </summary>
    public double MinElevationDeg { get; set; } = 20;
    /// <summary>Cap on the elevation-derived corridor, so high traffic far away never qualifies.</summary>
    public double MaxCorridorMetres { get; set; } = 5000;
    /// <summary>Keep the flight on the board this long after it has passed.</summary>
    public double HoldSeconds { get; set; } = 20;
    /// <summary>Never swap the board more often than this, even if traffic is tight.</summary>
    public double MinDisplaySeconds { get; set; } = 15;
    /// <summary>After a flight has passed, ignore that hex for this long (stops re-triggers on wobbly tracks).</summary>
    public double CooldownSeconds { get; set; } = 300;
    /// <summary>The condition must hold on this many consecutive polls before we commit (hysteresis).</summary>
    public int ConfirmPolls { get; set; } = 2;
    /// <summary>Forget an aircraft we have not heard from for this long.</summary>
    public double LostAfterSeconds { get; set; } = 60;
    /// <summary>Start warming the enrichment cache when an aircraft is within this many seconds of the house.</summary>
    public double PrefetchSeconds { get; set; } = 180;
    /// <summary>Ignore aircraft slower than this (taxiing, helicopters hovering, GA circuits).</summary>
    public double MinGroundSpeedKt { get; set; } = 30;
    /// <summary>Idle frame (clock / next flight) re-flips no more often than this.</summary>
    public double IdleRefreshSeconds { get; set; } = 60;

    public ApproachFilterOptions Approach { get; set; } = new();

    /// <summary>Lateral corridor for an aircraft at the given barometric altitude: max(CorridorMetres, alt / tan(MinElevationDeg)), capped.</summary>
    public double EffectiveCorridorMetres(int? altBaroFt)
    {
        if (MinElevationDeg <= 0 || altBaroFt is not { } alt || alt <= 0) return CorridorMetres;
        var byElevation = alt * 0.3048 / Math.Tan(MinElevationDeg * Math.PI / 180.0);
        return Math.Clamp(byElevation, CorridorMetres, Math.Max(CorridorMetres, MaxCorridorMetres));
    }
}

/// <summary>Distinguishes landing traffic on the approach from overflights and departures. Everything optional.</summary>
public sealed class ApproachFilterOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Aircraft above this (barometric feet) are overflights, not arrivals.</summary>
    public int? MaxAltitudeFt { get; set; } = 5000;
    /// <summary>Aircraft climbing faster than this (ft/min) are departures. Approaching traffic descends or is level.</summary>
    public int? MaxClimbFpm { get; set; } = 200;
    /// <summary>
    /// Runway headings the approach can be on, e.g. Gatwick 26L = 258, 08R = 78. Empty (the default) = any heading.
    /// Only worth enabling if the house is close to the extended centreline; further out, arrivals are still being vectored.
    /// </summary>
    public List<double> Headings { get; set; } = [];
    public double HeadingToleranceDeg { get; set; } = 25;
}
