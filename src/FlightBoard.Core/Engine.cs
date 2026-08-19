using FlightBoard.Core.Board;
using FlightBoard.Core.Enrichment;
using FlightBoard.Core.Geo;
using FlightBoard.Core.Interest;
using FlightBoard.Core.Model;
using FlightBoard.Core.Storage;
using FlightBoard.Core.Tracking;
using Microsoft.Extensions.Logging;

namespace FlightBoard.Core;

public sealed class QuietHoursOptions
{
    public bool Enabled { get; set; } = false;
    /// <summary>Local time "HH:mm" after which the board stays quiet.</summary>
    public string From { get; set; } = "23:00";
    public string To { get; set; } = "06:30";
}

public sealed record EngineState(
    DateTimeOffset LastPollAt,
    int TrackedCount,
    BoardMessage? Current,
    IReadOnlyList<TrackedFlight> Flights,
    string SourceName,
    bool Quiet);

/// <summary>
/// One tick = one poll: update the tracker, ask the scheduler what the board should say, enrich,
/// evaluate interest, render to every display, record the sighting. Also the entry point for the
/// demo/simulate API so the browser can be exercised without traffic.
/// </summary>
public sealed class Engine
{
    private readonly Tracker _tracker;
    private readonly BoardScheduler _scheduler;
    private readonly IFlightEnricher _enricher;
    private readonly InterestEvaluator _interest;
    private readonly CompositeDisplay _displays;
    private readonly SightingsRepo _sightings;
    private readonly Func<TrackerOptions> _trackerOptions;
    private readonly BoardOptions _board;
    private readonly InterestOptions _interestOptions;
    private readonly QuietHoursOptions _quiet;
    private readonly ILogger<Engine> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _prefetched = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeZoneInfo _tz;

    public Engine(
        GeoPoint home,
        Func<TrackerOptions> trackerOptions,
        BoardOptions board,
        InterestOptions interestOptions,
        QuietHoursOptions quiet,
        IFlightEnricher enricher,
        InterestEvaluator interest,
        CompositeDisplay displays,
        SightingsRepo sightings,
        ILoggerFactory loggers)
    {
        Home = home;
        _trackerOptions = trackerOptions;
        _board = board;
        _interestOptions = interestOptions;
        _quiet = quiet;
        _enricher = enricher;
        _interest = interest;
        _displays = displays;
        _sightings = sightings;
        _log = loggers.CreateLogger<Engine>();
        _tracker = new Tracker(home, trackerOptions, loggers.CreateLogger<Tracker>());
        _scheduler = new BoardScheduler(trackerOptions);
        try { _tz = TimeZoneInfo.FindSystemTimeZoneById(board.TimeZone); } catch { _tz = TimeZoneInfo.Local; }
    }

    public GeoPoint Home { get; }
    public Tracker Tracker => _tracker;
    public BoardMessage? Current { get; private set; }
    public DateTimeOffset LastPollAt { get; private set; }
    public string SourceName { get; set; } = "?";
    public event Action<BoardMessage>? Shown;

    public EngineState State => new(LastPollAt, _tracker.Flights.Count, Current,
        _tracker.Flights.OrderBy(f => double.IsNaN(f.TimeToCpaSeconds) ? double.MaxValue : Math.Abs(f.TimeToCpaSeconds)).ToList(),
        SourceName, IsQuiet(DateTimeOffset.UtcNow));

    public async Task TickAsync(SourcePoll poll, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            LastPollAt = poll.Timestamp;
            var tick = _tracker.Ingest(poll);

            foreach (var f in tick.PrefetchCandidates.Concat(tick.NewlyApproaching))
            {
                if (_prefetched.Add(f.Hex + "|" + f.Callsign))
                    _ = PreEvaluateAsync(f, poll.Timestamp, ct); // warm the cache and score interest so the scheduler can prioritise
            }
            if (_prefetched.Count > 500) _prefetched.Clear();

            var decision = _scheduler.Decide(_tracker.Flights, poll.Timestamp);
            switch (decision.Action)
            {
                case BoardAction.Show:
                    await ShowFlightAsync(decision.Flight!, poll.Timestamp, ct);
                    break;
                case BoardAction.Idle:
                    await ShowIdleAsync(poll.Timestamp, ct);
                    break;
                case BoardAction.None:
                    await RefreshQueueCountAsync(poll.Timestamp, ct);
                    break;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task ShowFlightAsync(TrackedFlight f, DateTimeOffset now, CancellationToken ct)
    {
        Enriched enriched;
        try { enriched = await _enricher.EnrichAsync(f.Hex, f.Callsign, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Enrichment failed for {Flight}", f);
            enriched = Enriched.Empty(f.Hex, f.Callsign);
        }

        var isDeparture = enriched.IsDepartureFrom(_interestOptions.HomeAirportIcao);
        if (isDeparture && !_board.ShowDepartures)
        {
            // A departure held level under the TMA. Not what the board is for: drop it and let the scheduler move on.
            _log.LogInformation("Skipping departure {Flight} to {Dest}", f, enriched.DestinationDisplay);
            f.Phase = FlightPhase.Cooldown;
            f.PhaseEnteredAt = now;
            _scheduler.MarkShown(null, now);
            return;
        }

        var interest = _interest.Evaluate(new InterestContext(f, enriched, _sightings, now, Home, _interestOptions));
        f.InterestScore = interest.Best?.Score ?? 0;
        f.InterestEvaluated = true;
        var queued = BoardScheduler.QueuedCount(_tracker.Flights, _trackerOptions(), f.Hex, _trackerOptions().PrefetchSeconds);
        var message = MessageBuilder.ForFlight(f, enriched, interest, _board, now, isDeparture) with { QueuedBehind = queued };

        _log.LogInformation("BOARD: {Flight} {Airline} {Dir} {Origin} [{Tag}] ({Hex} {Type} alt {Alt}ft, overhead in {T:0}s)",
            message.Flight, message.Airline, isDeparture ? "to" : "from", message.Origin, interest.Best?.Label ?? "-", f.Hex, f.Type, f.LastSample?.AltBaroFt, f.TimeToCpaSeconds);

        try
        {
            // OriginName holds what the board's third line said (origin for arrivals, destination for departures) so history replays verbatim.
            _sightings.Record(new Sighting(0, f.Hex, f.Callsign, message.Flight, message.Registration, Enriched.FirstNonEmpty(f.Type, enriched.TypeIcao),
                enriched.AirlineIcao, message.Airline, enriched.OriginIcao, message.Origin, interest.TagsCsv, f.LastSample?.AltBaroFt, now, isDeparture));
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not record sighting"); }

        await PublishAsync(message, FrameKind.Normal, ct);
    }

    /// <summary>Enrich and score a flight ahead of time so exciting ones can jump the queue. Errors are swallowed; the show path re-evaluates anyway.</summary>
    private async Task PreEvaluateAsync(TrackedFlight f, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var enriched = await _enricher.EnrichAsync(f.Hex, f.Callsign, ct);
            var interest = _interest.Evaluate(new InterestContext(f, enriched, _sightings, now, Home, _interestOptions));
            f.InterestScore = interest.Best?.Score ?? 0;
            f.InterestEvaluated = true;
            if (f.InterestScore >= InterestTag.AccentThreshold)
                _log.LogInformation("Priority inbound: {Flight} [{Tag}]", f, interest.Best!.Label);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Pre-evaluation failed for {Flight}", f);
        }
    }

    /// <summary>Keep the "+N" on a displayed flight honest as other aircraft join or leave the queue. Only the changed tiles flip.</summary>
    private async Task RefreshQueueCountAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (Current is null || Current.IsIdle || _scheduler.Shown is null) return;
        var queued = BoardScheduler.QueuedCount(_tracker.Flights, _trackerOptions(), _scheduler.Shown.Hex, _trackerOptions().PrefetchSeconds);
        if (queued == Current.QueuedBehind) return;
        await PublishAsync(Current with { QueuedBehind = queued }, FrameKind.Normal, ct);
    }

    private async Task ShowIdleAsync(DateTimeOffset now, CancellationToken ct)
    {
        var next = BoardScheduler.NextUp(_tracker.Flights, _trackerOptions());
        var message = MessageBuilder.Idle(_board, now, next, _sightings.CountToday(now));
        await PublishAsync(message, FrameKind.Normal, ct);
    }

    private async Task PublishAsync(BoardMessage message, FrameKind kind, CancellationToken ct)
    {
        Current = message;
        if (IsQuiet(message.ShownAt) && !message.IsIdle)
        {
            _log.LogDebug("Quiet hours: not flipping the board");
            return;
        }
        await _displays.ShowAsync(message, kind, ct);
        Shown?.Invoke(message);
    }

    /// <summary>Push an arbitrary flight onto the board right now (demo / testing the physical board).</summary>
    public async Task SimulateAsync(string flight, string airline, string origin, string? type, string? tagLabel, int tagScore, bool attract, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var tag = string.IsNullOrWhiteSpace(tagLabel) ? null : new InterestTag(tagLabel, tagScore, "manual");
            var message = new BoardMessage(MessageBuilder.SpaceFlightNumber(flight), airline, origin, null, type, tag, now);
            var fake = new TrackedFlight { Hex = "sim-" + Guid.NewGuid().ToString("N")[..6], Callsign = flight, Phase = FlightPhase.Overhead, PhaseEnteredAt = now };
            _scheduler.MarkShown(fake, now);
            Current = message;
            await _displays.ShowAsync(message, attract ? FrameKind.Attract : FrameKind.Normal, ct);
            Shown?.Invoke(message);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Put a past sighting back on every display (history replay). The next live flight replaces it as normal.</summary>
    public async Task ReplaySightingAsync(Sighting s, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var message = new BoardMessage(s.Flight ?? s.Callsign ?? s.Registration ?? s.Hex.ToUpperInvariant(), s.AirlineName ?? "", s.OriginName ?? "",
                s.Registration, _board.DisplayType(s.Type), TagFromCsv(s.Tags), now, IsDeparture: s.IsDeparture);
            var fake = new TrackedFlight { Hex = "replay-" + s.Id, Callsign = s.Callsign, Phase = FlightPhase.Overhead, PhaseEnteredAt = now };
            _scheduler.MarkShown(fake, now);
            Current = message;
            await _displays.ShowAsync(message, FrameKind.Normal, ct);
            Shown?.Invoke(message);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Best tag from the stored "category:LABEL,category:LABEL" list, with the score re-derived from the category.</summary>
    internal static InterestTag? TagFromCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        InterestTag? best = null;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = part.IndexOf(':');
            if (i <= 0) continue;
            var category = part[..i];
            var label = part[(i + 1)..];
            var score = category switch
            {
                Interest.Rules.Categories.Emergency => 100, Interest.Rules.Categories.Watch => 85, Interest.Rules.Categories.Military => 80,
                Interest.Rules.Categories.FirstSighting => 60, Interest.Rules.Categories.UnusualType => 50, Interest.Rules.Categories.Oddity => 40,
                Interest.Rules.Categories.Private => 30, _ => 50,
            };
            if (best is null || score > best.Score) best = new InterestTag(label, score, category);
        }
        return best;
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _scheduler.MarkShown(null, DateTimeOffset.UtcNow);
            Current = null;
            await _displays.ClearAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public bool IsQuiet(DateTimeOffset at)
    {
        if (!_quiet.Enabled) return false;
        if (!TimeOnly.TryParse(_quiet.From, out var from) || !TimeOnly.TryParse(_quiet.To, out var to)) return false;
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, _tz).DateTime);
        return from <= to ? local >= from && local < to : local >= from || local < to;
    }
}
