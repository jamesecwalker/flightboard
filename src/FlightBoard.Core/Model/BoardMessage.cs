using FlightBoard.Core.Interest;

namespace FlightBoard.Core.Model;

/// <summary>
/// The typed thing the engine wants the board to say. Displays render this to their own
/// row/column count via <see cref="Board.FrameLayout"/> — so the same message works on a
/// 2×16 DIY board, a 6×22 Vestaboard or the browser sim.
/// </summary>
public sealed record BoardMessage(
    string Flight,
    string Airline,
    string Origin,
    string? Registration,
    string? Type,
    InterestTag? Tag,
    DateTimeOffset ShownAt,
    bool IsIdle = false,
    string? IdleLine1 = null,
    string? IdleLine2 = null,
    string? IdleLine3 = null,
    bool IsDeparture = false,
    int QueuedBehind = 0,
    int? AltitudeFt = null,
    bool IsLow = false)
{
    public static BoardMessage Idle(DateTimeOffset now, string line1, string? line2 = null, string? line3 = null) =>
        new("", "", "", null, null, null, now, IsIdle: true, IdleLine1: line1, IdleLine2: line2, IdleLine3: line3);
}
