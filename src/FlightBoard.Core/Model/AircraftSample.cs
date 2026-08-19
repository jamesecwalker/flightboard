namespace FlightBoard.Core.Model;

/// <summary>One aircraft as reported by a readsb-style feed (adsb.lol, adsb.fi, a local dump1090) at one instant.</summary>
public sealed record AircraftSample(
    string Hex,
    string? Callsign,
    string? Registration,
    string? Type,
    string? Description,
    double? Lat,
    double? Lon,
    int? AltBaroFt,
    bool OnGround,
    double? GroundSpeedKt,
    double? TrackDeg,
    int? BaroRateFpm,
    string? Squawk,
    string? Emergency,
    string? Category,
    int DbFlags,
    double SeenPosSeconds,
    double SeenSeconds)
{
    public bool HasPosition => Lat.HasValue && Lon.HasValue;
    public bool HasVelocity => GroundSpeedKt.HasValue && TrackDeg.HasValue;
    /// <summary>readsb sets bit 0 for military, bit 1 interesting, bit 2 PIA, bit 3 LADD.</summary>
    public bool IsMilitaryFlagged => (DbFlags & 1) != 0;
    public bool IsEmergencySquawk => Squawk is "7700" or "7600" or "7500";
    public bool IsEmergency => IsEmergencySquawk || (!string.IsNullOrEmpty(Emergency) && Emergency != "none");
}

/// <summary>Result of one poll of a source: when it was taken (real or replayed) and what was seen.</summary>
public sealed record SourcePoll(DateTimeOffset Timestamp, IReadOnlyList<AircraftSample> Aircraft);
