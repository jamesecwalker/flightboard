using System.Text.Json;
using FlightBoard.Core.Geo;
using FlightBoard.Core.Model;
using FlightBoard.Core.Sources;
using FlightBoard.Core.Tracking;

namespace FlightBoard.Tests;

public class SourceTests
{
    private const string AdsbLolShape = """
        {"ac":[
          {"hex":"400aff","type":"adsb_icao","flight":"EFW48VJ ","r":"G-EUXG","t":"A321","alt_baro":8300,"gs":245.5,"track":243.64,"baro_rate":1984,"squawk":"5672","emergency":"none","category":"A3","lat":51.081940,"lon":-0.535287,"seen_pos":0.347,"seen":0.1,"dbFlags":0},
          {"hex":"407cb1","type":"adsb_icao","flight":"RUK41AV ","r":"G-RUKE","t":"B738","alt_baro":"ground","gs":12,"track":90,"lat":51.15,"lon":-0.18,"seen_pos":1,"seen":0.5},
          {"hex":"ab1234","type":"mlat","lat":51.2,"lon":-0.1,"alt_geom":3000,"seen":2}
        ]}
        """;

    private const string AdsbFiShape = """
        {"now": 1787125434.001, "aircraft":[
          {"hex":"400aff","flight":"EFW48VJ ","r":"G-EUXG","t":"A321","desc":"AIRBUS A-321","alt_baro":8275,"gs":245.5,"track":243.64,"baro_rate":1984,"squawk":"5672","emergency":"none","category":"A3","lat":51.082169,"lon":-0.534470,"seen_pos":0.349,"seen":0.1}
        ]}
        """;

    [Fact]
    public void Parses_adsb_lol_shape()
    {
        using var doc = JsonDocument.Parse(AdsbLolShape);
        var list = ReadsbJsonSource.Parse(doc.RootElement);
        Assert.Equal(3, list.Count);
        var a = list[0];
        Assert.Equal("400aff", a.Hex);
        Assert.Equal("EFW48VJ", a.Callsign);           // trimmed
        Assert.Equal("A321", a.Type);
        Assert.Equal(8300, a.AltBaroFt);
        Assert.Equal(245.5, a.GroundSpeedKt);
        Assert.Equal(1984, a.BaroRateFpm);
        Assert.False(a.OnGround);
        Assert.True(a.HasPosition && a.HasVelocity);
        var g = list[1];
        Assert.True(g.OnGround);
        var m = list[2];
        Assert.Null(m.Callsign);
        Assert.Equal(3000, m.AltBaroFt);               // falls back to alt_geom
        Assert.False(m.HasVelocity);
    }

    [Fact]
    public void Parses_adsb_fi_shape_with_description()
    {
        using var doc = JsonDocument.Parse(AdsbFiShape);
        var list = ReadsbJsonSource.Parse(doc.RootElement);
        Assert.Single(list);
        Assert.Equal("AIRBUS A-321", list[0].Description);
    }

    [Fact]
    public async Task Simulator_flies_a_plane_over_the_house_that_the_tracker_catches()
    {
        var start = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        var time = new FakeTime(start);
        var opts = new SimulatorOptions { IntervalSeconds = 90, FirstAfterSeconds = 0, InterestingEveryN = 0 };
        var source = new SimulatedSource(opts, Synthetic.Home, time);
        var o = new TrackerOptions();
        var tracker = new Tracker(Synthetic.Home, () => o);

        var approached = 0;
        for (var i = 0; i < 120; i++)
        {
            var poll = await source.PollAsync(CancellationToken.None);
            approached += tracker.Ingest(poll).NewlyApproaching.Count;
            time.Advance(TimeSpan.FromSeconds(2));
        }
        Assert.True(approached >= 2, $"expected at least two simulated arrivals in 4 minutes, got {approached}");
    }

    [Fact]
    public async Task Recording_round_trips_through_replay()
    {
        var path = Path.Combine(Path.GetTempPath(), "fb-test-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var polls = Synthetic.Polls([new Synthetic.Plane()], 10).ToList();
        await using (var rec = new RecordingSource(new ReplaySource(polls), path))
        {
            for (var i = 0; i < polls.Count; i++) await rec.PollAsync(CancellationToken.None);
        }
        var back = ReplaySource.Load(path).ToList();
        File.Delete(path);
        Assert.Equal(polls.Count, back.Count);
        Assert.Equal(polls[3].Timestamp, back[3].Timestamp);
        Assert.Equal(polls[3].Aircraft[0].Lat, back[3].Aircraft[0].Lat);
        Assert.Equal(polls[3].Aircraft[0].Callsign, back[3].Aircraft[0].Callsign);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
