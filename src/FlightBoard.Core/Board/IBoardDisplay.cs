namespace FlightBoard.Core.Board;

/// <summary>
/// The seam between the flight engine and any physical or virtual board.
/// A new board = one class implementing this; nothing else in Core changes.
/// </summary>
public interface IBoardDisplay
{
    string Name { get; }
    BoardCapabilities Capabilities { get; }
    Task ShowAsync(BoardFrame frame, CancellationToken ct);
}
