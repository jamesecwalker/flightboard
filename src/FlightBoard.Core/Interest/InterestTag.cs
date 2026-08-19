namespace FlightBoard.Core.Interest;

/// <summary>Why a flight is interesting. <see cref="Label"/> is what the board shows (kept short); <see cref="Score"/> picks the winner.</summary>
public sealed record InterestTag(string Label, int Score, string Category)
{
    /// <summary>Tags at or above this score get the accent (colour tiles / highlight) on the board.</summary>
    public const int AccentThreshold = 50;
    public bool Accent => Score >= AccentThreshold;
}
