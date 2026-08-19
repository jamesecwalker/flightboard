namespace FlightBoard.Core.Board;

/// <summary>Describes what a physical or virtual board can show. Core renders to this; adapters stay dumb.</summary>
public sealed record BoardCapabilities(
    int Rows,
    int Cols,
    string Charset,
    bool Colour,
    TimeSpan MinFlipInterval)
{
    /// <summary>Upper-case alphanumerics plus the punctuation most split-flap modules ship with.</summary>
    public const string DefaultCharset = " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-.,:*/'&?!+()";

    public static BoardCapabilities Default(int rows = 4, int cols = 22, bool colour = true) =>
        new(rows, cols, DefaultCharset, colour, TimeSpan.Zero);
}
