using FlightBoard.Core.Model;
using FlightBoard.Core.Tracking;

namespace FlightBoard.Tests;

public class TrackerTests
{
    private static (Tracker tracker, TrackerOptions options) NewTracker()
    {
        var o = Synthetic.Options();
        return (new Tracker(Synthetic.Home, () => o), o);
    }

    [Fact]
    public void Straight_overhead_triggers_inside_lead_window_then_passes()
    {
        var (tracker, o) = NewTracker();
        var plane = new Synthetic.Plane();
        double? tCpaAtTrigger = null, dCpaAtTrigger = null;
        var phases = new List<FlightPhase>();
        foreach (var poll in Synthetic.Polls([plane], 200))
        {
            var tick = tracker.Ingest(poll);
            if (tick.NewlyApproaching.Count > 0 && tCpaAtTrigger is null)
            {
                var fl = tick.NewlyApproaching.Single();
                tCpaAtTrigger = fl.TimeToCpaSeconds;
                dCpaAtTrigger = fl.CpaDistanceMetres;
            }
            var f = tracker.Get(plane.Hex);
            if (f is not null) phases.Add(f.Phase);
        }

        Assert.NotNull(tCpaAtTrigger);
        // Fires within one confirm-poll window of the lead time: Lead - (ConfirmPolls * 2 s) <= tCpa <= Lead.
        Assert.InRange(tCpaAtTrigger!.Value, o.LeadSeconds - o.ConfirmPolls * 2 - 1, o.LeadSeconds);
        Assert.InRange(dCpaAtTrigger!.Value, 0, 5);
        Assert.Contains(FlightPhase.Approaching, phases);
        Assert.Contains(FlightPhase.Overhead, phases);
        Assert.Contains(FlightPhase.Passed, phases);
        Assert.Contains(FlightPhase.Cooldown, phases);
        // Phases only move forwards.
        var order = phases.Distinct().ToList();
        Assert.Equal([FlightPhase.Idle, FlightPhase.Approaching, FlightPhase.Overhead, FlightPhase.Passed, FlightPhase.Cooldown], order);
    }

    [Fact]
    public void Near_miss_outside_corridor_never_triggers()
    {
        var (tracker, o) = NewTracker();
        var plane = new Synthetic.Plane { LateralOffsetM = o.MaxCorridorMetres + 400 };
        var any = Synthetic.Polls([plane], 200).Select(tracker.Ingest).Any(t => t.NewlyApproaching.Count > 0);
        Assert.False(any);
    }

    [Fact]
    public void Inside_corridor_but_off_centre_still_triggers()
    {
        var (tracker, o) = NewTracker();
        var plane = new Synthetic.Plane { LateralOffsetM = o.EffectiveCorridorMetres(2200) - 200 };
        var any = Synthetic.Polls([plane], 200).Select(tracker.Ingest).Any(t => t.NewlyApproaching.Count > 0);
        Assert.True(any);
    }

    [Fact]
    public void High_overflight_is_filtered_out()
    {
        var (tracker, _) = NewTracker();
        var plane = new Synthetic.Plane { AltOverHouseFt = 12000, BaroRateFpm = 0 };
        var any = Synthetic.Polls([plane], 200).Select(tracker.Ingest).Any(t => t.NewlyApproaching.Count > 0);
        Assert.False(any);
        Assert.Contains("alt", tracker.Get(plane.Hex)?.LastRejectReason ?? "");
    }

    [Fact]
    public void Climbing_departure_is_filtered_out()
    {
        var (tracker, _) = NewTracker();
        var plane = new Synthetic.Plane { AltOverHouseFt = 2500, BaroRateFpm = 2200 };
        var any = Synthetic.Polls([plane], 200).Select(tracker.Ingest).Any(t => t.NewlyApproaching.Count > 0);
        Assert.False(any);
    }

    [Fact]
    public void Wrong_heading_is_filtered_out_but_passes_when_headings_disabled()
    {
        var (tracker, o) = NewTracker();
        var plane = new Synthetic.Plane { TrackDeg = 180 };
        var any = Synthetic.Polls([plane], 200).Select(tracker.Ingest).Any(t => t.NewlyApproaching.Count > 0);
        Assert.False(any);

        var o2 = Synthetic.Options();
        o2.Approach.Headings.Clear();
        var tracker2 = new Tracker(Synthetic.Home, () => o2);
        var any2 = Synthetic.Polls([new Synthetic.Plane { TrackDeg = 180 }], 200).Select(tracker2.Ingest).Any(t => t.NewlyApproaching.Count > 0);
        Assert.True(any2);
    }

    [Fact]
    public void Two_aircraft_80s_apart_are_shown_in_order_without_preemption()
    {
        var (tracker, o) = NewTracker();
        var scheduler = new BoardScheduler(() => o);
        var a = new Synthetic.Plane { Hex = "aaaaaa", Callsign = "BAW1" };
        var b = new Synthetic.Plane { Hex = "bbbbbb", Callsign = "EZY2", StartOffsetSeconds = 80 };
        var shown = new List<(double t, string hex)>();
        var idles = 0;
        foreach (var poll in Synthetic.Polls([a, b], 400))
        {
            tracker.Ingest(poll);
            var d = scheduler.Decide(tracker.Flights, poll.Timestamp);
            if (d.Action == BoardAction.Show) shown.Add(((poll.Timestamp - Synthetic.T0).TotalSeconds, d.Flight!.Hex));
            if (d.Action == BoardAction.Idle) idles++;
        }
        Assert.Equal(["aaaaaa", "bbbbbb"], shown.Select(s => s.hex).ToArray());
        // B appears once A has gone overhead (not after A's hold), since B is already inside the lead window.
        Assert.True(shown[1].t - shown[0].t >= o.MinDisplaySeconds);
        Assert.True(shown[1].t - shown[0].t <= 80 + 6, $"B waited {shown[1].t - shown[0].t}s"); // B enters the window 80 s after A; it must not also wait out A's hold
        Assert.True(idles >= 1);
    }

    [Fact]
    public void Go_around_is_detected_and_rearms()
    {
        var (tracker, o) = NewTracker();
        // Approaches normally, then at 2000 ft starts climbing at 1500 fpm.
        var plane = new Synthetic.Plane
        {
            Mutate = (t, s) => s.AltBaroFt is < 2100 ? s with { BaroRateFpm = 1500, AltBaroFt = s.AltBaroFt + (int)(t * 2) } : s,
        };
        var goArounds = 0;
        foreach (var poll in Synthetic.Polls([plane], 200))
        {
            var tick = tracker.Ingest(poll);
            goArounds += tick.GoArounds.Count;
        }
        Assert.Equal(1, goArounds);
        var f = tracker.Get(plane.Hex);
        Assert.NotNull(f);
        Assert.True(f!.WentAround);
    }

    [Fact]
    public void Lost_mid_approach_is_treated_as_passed()
    {
        var (tracker, o) = NewTracker();
        var plane = new Synthetic.Plane();
        var polls = Synthetic.Polls([plane], 200).ToList();
        TrackedFlight? flight = null;
        var passedReported = false;
        foreach (var poll in polls)
        {
            // Once approaching, drop the aircraft from the feed entirely.
            var p = flight?.Phase == FlightPhase.Approaching ? new SourcePoll(poll.Timestamp, []) : poll;
            var tick = tracker.Ingest(p);
            if (tick.NewlyApproaching.Count > 0) flight = tick.NewlyApproaching[0];
            if (tick.NewlyPassed.Count > 0) passedReported = true;
        }
        Assert.NotNull(flight);
        Assert.True(passedReported);
    }

    [Fact]
    public void Emergency_and_military_flags_are_sticky()
    {
        var (tracker, _) = NewTracker();
        var plane = new Synthetic.Plane { Squawk = "7700", DbFlags = 1 };
        foreach (var poll in Synthetic.Polls([plane], 10)) tracker.Ingest(poll);
        var f = tracker.Get(plane.Hex)!;
        Assert.True(f.Emergency);
        Assert.True(f.MilitaryFlagged);
    }

    [Fact]
    public void Corridor_grows_with_altitude_up_to_the_cap()
    {
        var o = new TrackerOptions { CorridorMetres = 800, MinElevationDeg = 20, MaxCorridorMetres = 5000 };
        Assert.Equal(800, o.EffectiveCorridorMetres(null));
        Assert.Equal(800, o.EffectiveCorridorMetres(500));            // 152 m / tan20 = 419 m < floor
        Assert.InRange(o.EffectiveCorridorMetres(4500), 3700, 3800);  // 1372 m / tan20 = 3769 m
        Assert.Equal(5000, o.EffectiveCorridorMetres(9000));          // capped
        Assert.Equal(800, new TrackerOptions { CorridorMetres = 800, MinElevationDeg = 0 }.EffectiveCorridorMetres(6000));
    }

    [Fact]
    public void A_flight_is_shown_once_even_if_the_board_is_interrupted()
    {
        var (tracker, o) = NewTracker();
        var scheduler = new BoardScheduler(() => o);
        var plane = new Synthetic.Plane();
        var shows = 0;
        var interrupted = false;
        foreach (var poll in Synthetic.Polls([plane], 200))
        {
            tracker.Ingest(poll);
            var d = scheduler.Decide(tracker.Flights, poll.Timestamp);
            if (d.Action == BoardAction.Show) shows++;
            // Simulate a history replay / demo frame taking over the board mid-pass.
            if (shows == 1 && !interrupted && tracker.Get(plane.Hex)!.TimeToCpaSeconds < 20)
            {
                interrupted = true;
                scheduler.MarkShown(new TrackedFlight { Hex = "replay-1", Phase = FlightPhase.Overhead }, poll.Timestamp);
            }
        }
        Assert.True(interrupted);
        Assert.Equal(1, shows);
    }

    [Fact]
    public void Slow_first_aircraft_hands_over_as_soon_as_it_has_passed_overhead()
    {
        var (tracker, o) = NewTracker();
        var scheduler = new BoardScheduler(() => o);
        var piper = new Synthetic.Plane { Hex = "piper1", Callsign = "GCIZO", GroundSpeedKt = 100, AltOverHouseFt = 1900 };
        var jet = new Synthetic.Plane { Hex = "jet001", Callsign = "EZY21VA", StartOffsetSeconds = 70 }; // enters the window ~20 s after the Piper, passes ~20 s after it
        var shown = new List<(double t, string hex, double tCpa)>();
        foreach (var poll in Synthetic.Polls([piper, jet], 300))
        {
            tracker.Ingest(poll);
            var d = scheduler.Decide(tracker.Flights, poll.Timestamp);
            if (d.Action == BoardAction.Show) shown.Add(((poll.Timestamp - Synthetic.T0).TotalSeconds, d.Flight!.Hex, d.Flight.TimeToCpaSeconds));
        }
        Assert.Equal(["piper1", "jet001"], shown.Select(s => s.hex).ToArray());
        // The jet must be on the board before it is overhead, not after the Piper's hold has run out.
        Assert.True(shown[1].tCpa > 0, $"jet shown {shown[1].tCpa:0}s relative to overhead");
    }

    [Fact]
    public void Exciting_aircraft_jumps_the_queue_and_preempts_an_ordinary_inbound()
    {
        var (tracker, o) = NewTracker();
        var scheduler = new BoardScheduler(() => o);
        var ordinary = new Synthetic.Plane { Hex = "ord001", Callsign = "EZY1" };
        var raf = new Synthetic.Plane { Hex = "raf001", Callsign = "RRR1", Type = "A400", StartOffsetSeconds = 20 };
        var shown = new List<(double t, string hex, double tCpa)>();
        foreach (var poll in Synthetic.Polls([ordinary, raf], 300))
        {
            tracker.Ingest(poll);
            var r = tracker.Get(raf.Hex);
            if (r is not null && !r.InterestEvaluated) { r.InterestScore = 80; r.InterestEvaluated = true; }   // what PreEvaluateAsync would do
            var d = scheduler.Decide(tracker.Flights, poll.Timestamp);
            if (d.Action == BoardAction.Show) shown.Add(((poll.Timestamp - Synthetic.T0).TotalSeconds, d.Flight!.Hex, d.Flight.TimeToCpaSeconds));
        }
        Assert.Equal(["ord001", "raf001"], shown.Select(s => s.hex).ToArray());
        // The RAF flight took the board while the ordinary one was still inbound (after the minimum display time).
        Assert.InRange(shown[1].t - shown[0].t, o.MinDisplaySeconds, 25);
        Assert.True(shown[1].tCpa > 0);
    }

    [Fact]
    public void Queued_count_reflects_other_inbound_aircraft()
    {
        var (tracker, o) = NewTracker();
        var a = new Synthetic.Plane { Hex = "aaa001", Callsign = "A1" };
        var b = new Synthetic.Plane { Hex = "bbb001", Callsign = "B1", StartOffsetSeconds = 40 };
        var c = new Synthetic.Plane { Hex = "ccc001", Callsign = "C1", StartOffsetSeconds = 80 };
        var far = new Synthetic.Plane { Hex = "far001", Callsign = "F1", LateralOffsetM = 9000 };
        var max = 0;
        foreach (var poll in Synthetic.Polls([a, b, c, far], 120))
        {
            tracker.Ingest(poll);
            max = Math.Max(max, BoardScheduler.QueuedCount(tracker.Flights, o, "aaa001", o.PrefetchSeconds));
        }
        Assert.Equal(2, max);   // b and c queued behind a; the far one never counts
    }
}
