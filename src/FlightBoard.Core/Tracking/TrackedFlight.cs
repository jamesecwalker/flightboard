using FlightBoard.Core.Model;

namespace FlightBoard.Core.Tracking;

public enum FlightPhase
{
    /// <summary>Seen, but not (yet) heading over the house.</summary>
    Idle,
    /// <summary>Confirmed inbound: will pass within the corridor inside LeadSeconds.</summary>
    Approaching,
    /// <summary>Closest point of approach reached.</summary>
    Overhead,
    /// <summary>Gone by; still held on the board for HoldSeconds.</summary>
    Passed,
    /// <summary>Done; ignore this hex until the cooldown expires.</summary>
    Cooldown,
}

/// <summary>Everything the tracker knows about one hex over time.</summary>
public sealed class TrackedFlight
{
    public required string Hex { get; init; }
    public string? Callsign { get; set; }
    public string? Registration { get; set; }
    public string? Type { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Squawk { get; set; }
    public bool Emergency { get; set; }
    public bool MilitaryFlagged { get; set; }

    public AircraftSample? LastSample { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public DateTimeOffset LastPositionAt { get; set; }

    public FlightPhase Phase { get; set; } = FlightPhase.Idle;
    public DateTimeOffset PhaseEnteredAt { get; set; }

    /// <summary>Predicted seconds until closest approach (negative = past). NaN when unknown.</summary>
    public double TimeToCpaSeconds { get; set; } = double.NaN;
    /// <summary>Predicted lateral miss distance (m). NaN when unknown.</summary>
    public double CpaDistanceMetres { get; set; } = double.NaN;
    public double DistanceNowMetres { get; set; } = double.NaN;
    /// <summary>The corridor that applied at the last poll (grows with altitude when MinElevationDeg is set).</summary>
    public double CorridorMetres { get; set; } = double.NaN;
    /// <summary>Elevation above the horizon (deg) the aircraft will have at its closest point, from its current altitude.</summary>
    public double ElevationAtCpaDeg { get; set; } = double.NaN;
    /// <summary>Wall-clock time we predict the aircraft will be overhead.</summary>
    public DateTimeOffset? PredictedOverheadAt { get; set; }

    public int ConsecutiveCandidatePolls { get; set; }
    public string? LastRejectReason { get; set; }

    /// <summary>Set when a previously approaching aircraft climbs away below the ceiling - a go-around.</summary>
    public bool WentAround { get; set; }
    /// <summary>Skip the cooldown once so a go-around can trigger the board again on its second approach.</summary>
    public bool RearmPending { get; set; }
    public int Passes { get; set; }
    /// <summary>Already been on the board for this pass; the scheduler will not show it again (reset by a go-around re-arm).</summary>
    public bool WasShown { get; set; }
    /// <summary>Lowest barometric altitude seen while approaching, for go-around detection.</summary>
    public int? MinAltFt { get; set; }

    public bool IsCandidateForPrefetch(TrackerOptions o) =>
        Phase == FlightPhase.Idle && !double.IsNaN(TimeToCpaSeconds) &&
        TimeToCpaSeconds > 0 && TimeToCpaSeconds <= o.PrefetchSeconds && CpaDistanceMetres <= Math.Max(o.MaxCorridorMetres, o.CorridorMetres) * 1.5;

    public override string ToString() =>
        $"{Hex} {Callsign ?? "-"} {Type ?? "-"} {Phase} tCpa={TimeToCpaSeconds:0}s dCpa={CpaDistanceMetres:0}m alt={LastSample?.AltBaroFt}";
}
