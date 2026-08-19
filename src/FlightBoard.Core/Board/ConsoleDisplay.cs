namespace FlightBoard.Core.Board;

/// <summary>Draws the frame as a boxed grid on stdout. Handy for headless runs and for a Pi with no screen attached.</summary>
public sealed class ConsoleDisplay : IBoardDisplay
{
    private readonly TextWriter _out;
    public ConsoleDisplay(BoardCapabilities caps, TextWriter? writer = null)
    {
        Capabilities = caps;
        _out = writer ?? Console.Out;
    }

    public string Name => "console";
    public BoardCapabilities Capabilities { get; }

    public Task ShowAsync(BoardFrame frame, CancellationToken ct)
    {
        var bar = "+" + new string('-', Capabilities.Cols + 2) + "+";
        _out.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} [{frame.Kind}]");
        _out.WriteLine(bar);
        for (var r = 0; r < frame.Rows.Length; r++)
        {
            var text = frame.Rows[r].PadRight(Capabilities.Cols);
            var accent = frame.Accent is not null && r < frame.Accent.Length && frame.Accent[r].Any(a => a) ? "*" : " ";
            _out.WriteLine($"|{accent}{text} |");
        }
        _out.WriteLine(bar);
        return Task.CompletedTask;
    }
}
