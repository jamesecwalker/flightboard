namespace FlightBoard.Core.Tracking;

public enum BoardAction { None, Show, Idle }

public sealed record BoardDecision(BoardAction Action, TrackedFlight? Flight)
{
    public static readonly BoardDecision None = new(BoardAction.None, null);
}

/// <summary>
/// Decides what is on the board given the tracker's view. Handles the normal Gatwick case of
/// arrivals 90-120 s apart: never pre-empt a shown flight with a later one, but the moment the
/// shown flight is gone show the next if it is already inside the lead window, else go idle.
/// </summary>
public sealed class BoardScheduler
{
    private readonly Func<TrackerOptions> _options;

    public BoardScheduler(Func<TrackerOptions> options) => _options = options;

    public TrackedFlight? Shown { get; private set; }
    public DateTimeOffset ShownAt { get; private set; }
    public bool IsIdle { get; private set; } = true;
    public DateTimeOffset LastIdleFrameAt { get; private set; } = DateTimeOffset.MinValue;

    public BoardDecision Decide(IEnumerable<TrackedFlight> flights, DateTimeOffset now)
    {
        var o = _options();
        var eligible = flights
            .Where(IsEligible)
            .OrderBy(f => f.TimeToCpaSeconds)
            .ToList();

        if (Shown is not null)
        {
            var stillEligible = eligible.Any(f => f.Hex == Shown.Hex);
            var onBoardFor = (now - ShownAt).TotalSeconds;
            if (stillEligible || onBoardFor < o.MinDisplaySeconds) return BoardDecision.None;

            var next = eligible.FirstOrDefault(f => f.Hex != Shown.Hex);
            if (next is not null) return Show(next, now);
            Shown = null;
            IsIdle = true;
            LastIdleFrameAt = now;
            return new BoardDecision(BoardAction.Idle, null);
        }

        var head = eligible.FirstOrDefault();
        if (head is not null) return Show(head, now);

        if (IsIdle && (now - LastIdleFrameAt).TotalSeconds >= o.IdleRefreshSeconds)
        {
            LastIdleFrameAt = now;
            return new BoardDecision(BoardAction.Idle, null);
        }
        return BoardDecision.None;
    }

    /// <summary>Force something onto the board (e.g. from /api/simulate) so the scheduler knows about it.</summary>
    public void MarkShown(TrackedFlight? flight, DateTimeOffset now)
    {
        Shown = flight;
        ShownAt = now;
        IsIdle = flight is null;
        if (flight is null) LastIdleFrameAt = now;
    }

    /// <summary>The next thing coming, for the idle "NEXT ... IN N MIN" line.</summary>
    public static TrackedFlight? NextUp(IEnumerable<TrackedFlight> flights, TrackerOptions o) =>
        flights.Where(f => f.Phase == FlightPhase.Idle && !double.IsNaN(f.TimeToCpaSeconds)
                           && f.TimeToCpaSeconds > 0 && f.CpaDistanceMetres <= (double.IsNaN(f.CorridorMetres) ? o.CorridorMetres : f.CorridorMetres) * 1.5
                           && f.LastRejectReason is null && f.LastSample is not null && !f.LastSample.OnGround)
               .OrderBy(f => f.TimeToCpaSeconds)
               .FirstOrDefault();

    private static bool IsEligible(TrackedFlight f) =>
        f.Phase is FlightPhase.Approaching or FlightPhase.Overhead or FlightPhase.Passed;

    private BoardDecision Show(TrackedFlight f, DateTimeOffset now)
    {
        Shown = f;
        ShownAt = now;
        IsIdle = false;
        return new BoardDecision(BoardAction.Show, f);
    }
}
