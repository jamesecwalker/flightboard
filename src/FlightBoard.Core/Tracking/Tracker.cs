using FlightBoard.Core.Geo;
using FlightBoard.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlightBoard.Core.Tracking;

/// <summary>What changed during one Ingest - for logging, prefetching and the oddities rule.</summary>
public sealed record TrackerTick(
    DateTimeOffset At,
    IReadOnlyList<TrackedFlight> NewlyApproaching,
    IReadOnlyList<TrackedFlight> NewlyPassed,
    IReadOnlyList<TrackedFlight> GoArounds,
    IReadOnlyList<TrackedFlight> PrefetchCandidates);

/// <summary>
/// Pure state machine: feed it polls, it works out which aircraft are about to fly over the house.
/// No IO, no clocks - the poll timestamp is "now", so recordings replay deterministically.
/// </summary>
public sealed class Tracker
{
    private readonly LocalPlane _plane;
    private readonly Func<TrackerOptions> _options;
    private readonly ILogger _log;
    private readonly Dictionary<string, TrackedFlight> _flights = new(StringComparer.OrdinalIgnoreCase);

    public Tracker(GeoPoint home, Func<TrackerOptions> options, ILogger? log = null)
    {
        _plane = new LocalPlane(home);
        _options = options;
        _log = log ?? NullLogger.Instance;
    }

    public GeoPoint Home => _plane.Origin;
    public IReadOnlyCollection<TrackedFlight> Flights => _flights.Values;

    public TrackedFlight? Get(string hex) => _flights.GetValueOrDefault(hex);

    public TrackerTick Ingest(SourcePoll poll)
    {
        var o = _options();
        var now = poll.Timestamp;
        var approaching = new List<TrackedFlight>();
        var passed = new List<TrackedFlight>();
        var goArounds = new List<TrackedFlight>();
        var prefetch = new List<TrackedFlight>();

        foreach (var s in poll.Aircraft)
        {
            if (string.IsNullOrWhiteSpace(s.Hex)) continue;
            if (!_flights.TryGetValue(s.Hex, out var f))
            {
                f = new TrackedFlight { Hex = s.Hex, FirstSeen = now, PhaseEnteredAt = now };
                _flights[s.Hex] = f;
            }
            Update(f, s, now, o, approaching, passed, goArounds);
            if (f.IsCandidateForPrefetch(o)) prefetch.Add(f);
        }

        // Age out flights that were not in this poll.
        foreach (var f in _flights.Values.ToList())
        {
            var silentFor = (now - f.LastSeen).TotalSeconds;
            if (silentFor > o.LostAfterSeconds)
            {
                if (f.Phase is FlightPhase.Approaching or FlightPhase.Overhead) { passed.Add(f); Enter(f, FlightPhase.Passed, now); }
                _flights.Remove(f.Hex);
                continue;
            }
            if (f.Phase is FlightPhase.Approaching or FlightPhase.Overhead && silentFor > 15)
            {
                // Lost mid-approach (blind spot / receiver gap): treat as passed so the board does not stick.
                passed.Add(f);
                Enter(f, FlightPhase.Passed, now);
            }
            if (f.Phase == FlightPhase.Passed && (now - f.PhaseEnteredAt).TotalSeconds > o.HoldSeconds)
                Enter(f, FlightPhase.Cooldown, now);
            if (f.Phase == FlightPhase.Cooldown && ((now - f.PhaseEnteredAt).TotalSeconds > o.CooldownSeconds || f.RearmPending))
            {
                f.RearmPending = false;
                f.WasShown = false;
                Enter(f, FlightPhase.Idle, now);
            }
        }

        return new TrackerTick(now, approaching, passed, goArounds, prefetch);
    }

    private void Update(TrackedFlight f, AircraftSample s, DateTimeOffset now, TrackerOptions o,
        List<TrackedFlight> approaching, List<TrackedFlight> passed, List<TrackedFlight> goArounds)
    {
        f.LastSeen = now;
        f.LastSample = s;
        var cs = s.Callsign?.Trim();
        if (!string.IsNullOrEmpty(cs)) f.Callsign = cs;
        f.Registration ??= s.Registration;
        f.Type ??= s.Type;
        f.Description ??= s.Description;
        f.Category ??= s.Category;
        f.Squawk = s.Squawk ?? f.Squawk;
        f.Emergency |= s.IsEmergency;
        f.MilitaryFlagged |= s.IsMilitaryFlagged;

        if (!s.HasPosition || !s.HasVelocity || s.OnGround || (s.GroundSpeedKt ?? 0) < o.MinGroundSpeedKt)
        {
            f.ConsecutiveCandidatePolls = 0;
            return;
        }
        f.LastPositionAt = now;

        // Dead-reckon the reported position forward by its age, then CPA to home.
        var (ae, an) = _plane.ToLocal(new GeoPoint(s.Lat!.Value, s.Lon!.Value));
        var (ve, vn) = LocalPlane.Velocity(s.GroundSpeedKt!.Value, s.TrackDeg!.Value);
        var age = Math.Clamp(s.SeenPosSeconds, 0, 30);
        ae += ve * age;
        an += vn * age;
        var cpa = Cpa.Compute(-ae, -an, ve, vn);
        f.TimeToCpaSeconds = cpa.TimeToCpaSeconds;
        f.CpaDistanceMetres = cpa.CpaDistanceMetres;
        f.DistanceNowMetres = cpa.CurrentDistanceMetres;
        f.PredictedOverheadAt = cpa.IsMoving ? now.AddSeconds(cpa.TimeToCpaSeconds) : null;
        var corridor = o.EffectiveCorridorMetres(s.AltBaroFt);
        f.CorridorMetres = corridor;
        f.ElevationAtCpaDeg = s.AltBaroFt is { } altFt ? Math.Atan2(altFt * 0.3048, Math.Max(cpa.CpaDistanceMetres, 1)) * 180.0 / Math.PI : double.NaN;

        var reject = ApproachFilter.Reject(s, o.Approach);
        f.LastRejectReason = reject;

        // Go-around: was on the approach, low, and is now climbing hard.
        if (f.Phase != FlightPhase.Idle && !f.WentAround
            && s.BaroRateFpm is > 800 && s.AltBaroFt is < 3500 && f.MinAltFt is < 3000)
        {
            f.WentAround = true;
            f.RearmPending = true;
            goArounds.Add(f);
            _log.LogInformation("Go-around detected: {Flight}", f);
            if (f.Phase is FlightPhase.Approaching or FlightPhase.Overhead)
            {
                passed.Add(f);
                Enter(f, FlightPhase.Passed, now);
            }
            // Re-arm so the second attempt triggers the board again.
            if (f.Phase == FlightPhase.Cooldown) { f.RearmPending = false; f.WasShown = false; Enter(f, FlightPhase.Idle, now); }
        }

        switch (f.Phase)
        {
            case FlightPhase.Idle:
            {
                var geometric = cpa.IsMoving && cpa.TimeToCpaSeconds > 0 && cpa.TimeToCpaSeconds <= o.LeadSeconds
                                && cpa.CpaDistanceMetres <= corridor;
                if (geometric && reject is not null)
                    _log.LogDebug("Candidate rejected ({Reason}): {Flight}", reject, f);
                if (geometric && reject is null)
                {
                    f.ConsecutiveCandidatePolls++;
                    if (f.ConsecutiveCandidatePolls >= o.ConfirmPolls)
                    {
                        Enter(f, FlightPhase.Approaching, now);
                        f.MinAltFt = s.AltBaroFt;
                        f.Passes++;
                        approaching.Add(f);
                        _log.LogInformation("Approaching: {Flight}", f);
                    }
                }
                else f.ConsecutiveCandidatePolls = 0;
                break;
            }
            case FlightPhase.Approaching:
                if (s.AltBaroFt is { } a && (f.MinAltFt is null || a < f.MinAltFt)) f.MinAltFt = a;
                if (cpa.TimeToCpaSeconds <= 0) Enter(f, FlightPhase.Overhead, now);
                break;
            case FlightPhase.Overhead:
                if (cpa.TimeToCpaSeconds < -o.HoldSeconds || cpa.CurrentDistanceMetres > corridor * 4)
                {
                    Enter(f, FlightPhase.Passed, now);
                    passed.Add(f);
                }
                break;
            case FlightPhase.Passed:
            case FlightPhase.Cooldown:
                break;
        }
    }

    private static void Enter(TrackedFlight f, FlightPhase phase, DateTimeOffset now)
    {
        f.Phase = phase;
        f.PhaseEnteredAt = now;
        f.ConsecutiveCandidatePolls = 0;
    }
}
