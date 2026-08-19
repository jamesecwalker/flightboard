namespace FlightBoard.Core.Board;

public enum FrameKind
{
    /// <summary>Ordinary content update.</summary>
    Normal,
    /// <summary>Flip every tile once (attract mode / test) then settle on the content.</summary>
    Attract,
    /// <summary>Blank the board.</summary>
    Clear,
}

/// <summary>
/// The lowest-level thing a display understands: a rows×cols grid of characters (already
/// transliterated to the board's charset) plus optional per-cell accent flags.
/// </summary>
public sealed record BoardFrame(string[] Rows, bool[][]? Accent, FrameKind Kind = FrameKind.Normal)
{
    public static BoardFrame Blank(BoardCapabilities caps) =>
        new(Enumerable.Repeat(new string(' ', caps.Cols), caps.Rows).ToArray(), null, FrameKind.Clear);

    public bool HasAccent => Accent is not null && Accent.Any(r => r.Any(c => c));
}
