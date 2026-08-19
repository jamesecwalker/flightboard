using System.Text.Json;
using FlightBoard.Core.Model;

namespace FlightBoard.Core.Sources;

/// <summary>
/// Plays back a jsonl recording made by <see cref="RecordingSource"/>. Poll timestamps are the
/// recorded ones, so the tracker behaves exactly as it did live - which is what makes tuning
/// against a real day's traffic possible.
/// </summary>
public sealed class ReplaySource : IAircraftSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Queue<SourcePoll> _polls;
    private readonly double _speed;
    private DateTimeOffset? _lastTs;

    public ReplaySource(string path, double speed = 1) : this(Load(path), speed) { }

    public ReplaySource(IEnumerable<SourcePoll> polls, double speed = 0)
    {
        _polls = new Queue<SourcePoll>(polls);
        _speed = speed;
        Total = _polls.Count;
    }

    public string Name => "replay";
    public int Total { get; }
    public int Remaining => _polls.Count;
    public bool Finished => _polls.Count == 0;

    public async Task<SourcePoll> PollAsync(CancellationToken ct)
    {
        if (_polls.Count == 0) return new SourcePoll(_lastTs ?? DateTimeOffset.UtcNow, []);
        var poll = _polls.Dequeue();
        if (_speed > 0 && _lastTs is { } last)
        {
            var gap = (poll.Timestamp - last) / _speed;
            if (gap > TimeSpan.Zero && gap < TimeSpan.FromMinutes(1)) await Task.Delay(gap, ct);
        }
        _lastTs = poll.Timestamp;
        return poll;
    }

    public static IEnumerable<SourcePoll> Load(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var poll = JsonSerializer.Deserialize<SourcePoll>(line, Json);
            if (poll is not null) yield return poll;
        }
    }
}
