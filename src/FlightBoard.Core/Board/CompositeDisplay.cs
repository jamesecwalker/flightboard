using Microsoft.Extensions.Logging;

namespace FlightBoard.Core.Board;

/// <summary>Fans a message out to several displays; each renders to its own capabilities. One failing display never blocks the others.</summary>
public sealed class CompositeDisplay
{
    private readonly IReadOnlyList<IBoardDisplay> _displays;
    private readonly ILogger<CompositeDisplay> _log;

    public CompositeDisplay(IEnumerable<IBoardDisplay> displays, ILogger<CompositeDisplay> log)
    {
        _displays = displays.ToList();
        _log = log;
    }

    public IReadOnlyList<IBoardDisplay> Displays => _displays;

    public async Task ShowAsync(Model.BoardMessage message, FrameKind kind, CancellationToken ct)
    {
        var tasks = _displays.Select(async d =>
        {
            try
            {
                var frame = FrameLayout.Render(message, d.Capabilities) with { Kind = kind };
                await d.ShowAsync(frame, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Display {Display} failed to show frame", d.Name);
            }
        });
        await Task.WhenAll(tasks);
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        foreach (var d in _displays)
        {
            try { await d.ShowAsync(BoardFrame.Blank(d.Capabilities), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "Display {Display} failed to clear", d.Name); }
        }
    }
}
