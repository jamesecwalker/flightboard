using FlightBoard.Core.Enrichment;
using FlightBoard.Core.Interest;
using FlightBoard.Core.Interest.Rules;
using FlightBoard.Core.Storage;
using FlightBoard.Core.Tracking;

namespace FlightBoard.Tests;

public class RulesTests
{
    private sealed class FakeSightings : ISightings
    {
        public HashSet<string> Hexes = new(), Airlines = new(), Origins = new(), Types = new();
        public DateTimeOffset FirstRunAt { get; set; } = DateTimeOffset.UtcNow.AddDays(-30);
        public bool HasSeenHex(string hex) => Hexes.Contains(hex);
        public bool HasSeenAirline(string a) => Airlines.Contains(a);
        public bool HasSeenOrigin(string o) => Origins.Contains(o);
        public bool HasSeenType(string t) => Types.Contains(t);
        public int CountToday(DateTimeOffset now) => 0;
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly InterestEvaluator Evaluator = new(InterestEvaluator.DefaultRules());

    private static InterestContext Ctx(TrackedFlight f, Enriched? e = null, FakeSightings? s = null, InterestOptions? o = null) =>
        // Default sightings store is still in its warm-up week, so first-sighting tags stay out of the way unless a test opts in.
        new(f, e ?? Enriched.Empty(f.Hex, f.Callsign), s ?? new FakeSightings { FirstRunAt = Now.AddDays(-1) }, Now, Synthetic.Home, o ?? new InterestOptions());

    private static Enriched Route(string hex, string callsign, string airline, string originIcao, double lat, double lon, string dest = "EGKK") =>
        Enriched.Empty(hex, callsign) with { AirlineIcao = airline, AirlineName = airline, OriginIcao = originIcao, OriginLat = lat, OriginLon = lon, DestinationIcao = dest, RouteFound = true };

    [Fact]
    public void Emergency_squawk_wins_over_everything()
    {
        var f = new TrackedFlight { Hex = "43c6e2", Callsign = "RRR4321", Type = "A388", Squawk = "7700", Emergency = true };
        var r = Evaluator.Evaluate(Ctx(f));
        Assert.Equal("EMERGENCY", r.Best!.Label);
        Assert.Equal(100, r.Best.Score);
        Assert.Contains(r.All, t => t.Category == Categories.Military);
        Assert.Contains(r.All, t => t.Category == Categories.UnusualType);
    }

    [Fact]
    public void Raf_callsign_is_military()
    {
        var f = new TrackedFlight { Hex = "400abc", Callsign = "RRR4321" };
        var r = Evaluator.Evaluate(Ctx(f));
        Assert.Equal("RAF", r.Best!.Label);
        Assert.True(r.Best.Accent);
    }

    [Fact]
    public void Uk_military_hex_range_is_military()
    {
        var f = new TrackedFlight { Hex = "43c123", Callsign = "ZZ123" };
        var r = Evaluator.Evaluate(Ctx(f));
        Assert.Equal("UK MILITARY", r.Best!.Label);
    }

    [Fact]
    public void A380_is_unusual_type()
    {
        var f = new TrackedFlight { Hex = "896427", Callsign = "UAE15", Type = "A388" };
        var r = Evaluator.Evaluate(Ctx(f));
        Assert.Equal("A380", r.Best!.Label);
        Assert.True(r.Best.Accent);
    }

    [Fact]
    public void Helicopter_category_is_unusual()
    {
        var f = new TrackedFlight { Hex = "407cba", Callsign = "GDOUN", Category = "A7" };
        var r = Evaluator.Evaluate(Ctx(f));
        Assert.Equal("HELICOPTER", r.Best!.Label);
    }

    [Fact]
    public void Ordinary_easyjet_is_not_interesting()
    {
        var f = new TrackedFlight { Hex = "406a3b", Callsign = "EZY8123", Type = "A320", Category = "A3" };
        var e = Route(f.Hex, "EZY8123", "EZY", "LEAL", 38.28, -0.56);
        var r = Evaluator.Evaluate(Ctx(f, e, new FakeSightings { Hexes = { f.Hex }, Airlines = { "EZY" }, Origins = { "LEAL" }, Types = { "A320" } }));
        Assert.Null(r.Best);
    }

    [Fact]
    public void First_sightings_only_after_warmup()
    {
        var f = new TrackedFlight { Hex = "aaaaaa", Callsign = "XYZ123", Type = "A320" };
        var e = Route(f.Hex, "XYZ123", "XYZ", "ZZZZ", 50, 0);
        var fresh = new FakeSightings { FirstRunAt = Now.AddDays(-1) };
        Assert.Null(Evaluator.Evaluate(Ctx(f, e, fresh)).Best);

        var warmed = new FakeSightings { FirstRunAt = Now.AddDays(-30) };
        var r = Evaluator.Evaluate(Ctx(f, e, warmed));
        Assert.Equal("NEW AIRLINE", r.Best!.Label);
        Assert.Contains(r.All, t => t.Label == "NEW ROUTE");
        Assert.Contains(r.All, t => t.Label == "FIRST VISIT");
        Assert.Contains(r.All, t => t.Label.StartsWith("NEW TYPE"));
    }

    [Fact]
    public void Bizjet_with_no_route_is_private()
    {
        var f = new TrackedFlight { Hex = "4b1a11", Callsign = null, Type = "GLF6", Category = "A2" };
        var r = Evaluator.Evaluate(Ctx(f));
        Assert.Equal("PRIVATE JET", r.Best!.Label);
        Assert.False(r.Best.Accent);
    }

    [Fact]
    public void Bizjet_type_with_a_scheduled_route_is_not_private()
    {
        var f = new TrackedFlight { Hex = "4b1a11", Callsign = "NJE123", Type = "C68A", Category = "A2" };
        var e = Route(f.Hex, "NJE123", "NJE", "LFPB", 48.97, 2.44);
        var r = Evaluator.Evaluate(Ctx(f, e));
        Assert.DoesNotContain(r.All, t => t.Category == Categories.Private);
    }

    [Fact]
    public void Long_haul_and_diversion_are_oddities()
    {
        var f = new TrackedFlight { Hex = "400b1c", Callsign = "TOM52", Type = "B789", Category = "A5" };
        var longHaul = Route(f.Hex, "TOM52", "TOM", "VTBS", 13.69, 100.75); // Bangkok, ~9,500 km
        var r = Evaluator.Evaluate(Ctx(f, longHaul));
        Assert.StartsWith("LONG HAUL", r.Best!.Label);

        var diverted = Route(f.Hex, "TOM52", "TOM", "EGLL", 51.47, -0.46, dest: "EGLL");
        Assert.Null(Evaluator.Evaluate(Ctx(f, diverted)).Best);                       // off by default (multi-leg routes)
        r = Evaluator.Evaluate(Ctx(f, diverted, o: new InterestOptions { DetectDiversions = true }));
        Assert.Equal("DIVERTED", r.Best!.Label);
    }

    [Fact]
    public void Go_around_is_tagged()
    {
        var f = new TrackedFlight { Hex = "406a3b", Callsign = "EZY8123", Type = "A320", WentAround = true };
        var r = Evaluator.Evaluate(Ctx(f));
        Assert.Equal("GO AROUND", r.Best!.Label);
    }

    [Fact]
    public void Watch_list_registration_is_flagged()
    {
        var o = new InterestOptions();
        o.WatchRegistrations["G-XLEB"] = "ROYAL";
        var f = new TrackedFlight { Hex = "400000", Callsign = "KRF21", Registration = "G-XLEB" };
        var r = Evaluator.Evaluate(Ctx(f, o: o));
        Assert.Equal("ROYAL", r.Best!.Label);
    }
}
