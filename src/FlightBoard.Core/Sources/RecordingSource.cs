using System.Text.Json;
using FlightBoard.Core.Model;

namespace FlightBoard.Core.Sources;

/// <summary>Decorator: passes polls through and appends each one to a jsonl file for later replay/tuning.</summary>
public sealed class RecordingSource : IAircraftSource, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly IAircraftSource _inner;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecordingSource(IAircraftSource inner, string path)
    {
        _inner = inner;
        var resolved = path.Replace("{date}", DateTime.UtcNow.ToString("yyyyMMdd-HHmm"));
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(resolved));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _writer = new StreamWriter(new FileStream(resolved, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        Path = resolved;
    }

    public string Path { get; }
    public string Name => _inner.Name + "+rec";

    public async Task<SourcePoll> PollAsync(CancellationToken ct)
    {
        var poll = await _inner.PollAsync(ct);
        await _gate.WaitAsync(ct);
        try { await _writer.WriteLineAsync(JsonSerializer.Serialize(poll, Json)); }
        finally { _gate.Release(); }
        return poll;
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _gate.Dispose();
    }
}
