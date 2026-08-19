using FlightBoard.Core.Model;

namespace FlightBoard.Core.Sources;

public interface IAircraftSource
{
    string Name { get; }
    Task<SourcePoll> PollAsync(CancellationToken ct);
}

public sealed class SourceOptions
{
    /// <summary>readsb | simulated | replay</summary>
    public string Kind { get; set; } = "simulated";
    /// <summary>
    /// readsb-format endpoints, tried in order with failover. {lat} {lon} {nm} are substituted.
    /// A local receiver works too: http://pi:8080/data/aircraft.json
    /// </summary>
    public List<string> Urls { get; set; } =
    [
        "https://api.adsb.lol/v2/lat/{lat}/lon/{lon}/dist/{nm}",
        "https://opendata.adsb.fi/api/v2/lat/{lat}/lon/{lon}/dist/{nm}",
    ];
    public double RadiusNm { get; set; } = 25;
    public double PollSeconds { get; set; } = 2;
    /// <summary>When set, every poll is appended to this jsonl file (for replay/tuning). Supports {date}.</summary>
    public string? RecordTo { get; set; }
    /// <summary>Replay: file to play back.</summary>
    public string? ReplayFile { get; set; }
    /// <summary>Replay: 1 = real time, 10 = ten times faster, 0 = as fast as possible.</summary>
    public double ReplaySpeed { get; set; } = 1;
    public SimulatorOptions Simulator { get; set; } = new();
}

public sealed class SimulatorOptions
{
    /// <summary>Seconds between simulated arrivals.</summary>
    public double IntervalSeconds { get; set; } = 90;
    /// <summary>Track the simulated aircraft fly (deg). 258 = Gatwick 26L approach, coming from the east.</summary>
    public double TrackDeg { get; set; } = 258;
    public double GroundSpeedKt { get; set; } = 150;
    /// <summary>Altitude when passing over the house (ft).</summary>
    public int AltitudeOverHouseFt { get; set; } = 2200;
    /// <summary>Distance out from the house at which each aircraft appears (km).</summary>
    public double SpawnDistanceKm { get; set; } = 12;
    /// <summary>Lateral offset of the flight path from the house (m); positive = right of track. Set to e.g. 400 to test the corridor.</summary>
    public double LateralOffsetMetres { get; set; } = 0;
    /// <summary>1 in N simulated flights is "interesting" (A380, RAF, emergency...).</summary>
    public int InterestingEveryN { get; set; } = 4;
    /// <summary>Spawn the first aircraft this many seconds after start.</summary>
    public double FirstAfterSeconds { get; set; } = 10;
}
