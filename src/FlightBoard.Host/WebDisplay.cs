using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using FlightBoard.Core.Board;

namespace FlightBoard.Host;

/// <summary>
/// The browser split-flap simulation. Frames are pushed to every connected page over Server-Sent Events;
/// a page that connects late gets the current frame straight away.
/// </summary>
public sealed class WebDisplay : IBoardDisplay
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();
    private string? _lastEvent;

    public WebDisplay(BoardCapabilities caps) => Capabilities = caps;

    public string Name => "web";
    public BoardCapabilities Capabilities { get; }
    public int ClientCount => _clients.Count;

    public Task ShowAsync(BoardFrame frame, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { rows = frame.Rows, accent = frame.Accent, kind = frame.Kind.ToString().ToLowerInvariant() }, Json);
        var evt = $"event: frame\ndata: {payload}\n\n";
        _lastEvent = evt;
        foreach (var c in _clients.Values) c.Writer.TryWrite(evt);
        return Task.CompletedTask;
    }

    public async Task StreamAsync(HttpContext ctx)
    {
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        _clients[id] = channel;
        try
        {
            var caps = JsonSerializer.Serialize(new { rows = Capabilities.Rows, cols = Capabilities.Cols, charset = Capabilities.Charset, colour = Capabilities.Colour }, Json);
            await ctx.Response.WriteAsync($"event: caps\ndata: {caps}\n\n", ctx.RequestAborted);
            if (_lastEvent is not null) await ctx.Response.WriteAsync(_lastEvent, ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

            using var keepalive = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var readTask = channel.Reader.ReadAsync(ctx.RequestAborted).AsTask();
            var tickTask = keepalive.WaitForNextTickAsync(ctx.RequestAborted).AsTask();
            while (!ctx.RequestAborted.IsCancellationRequested)
            {
                var done = await Task.WhenAny(readTask, tickTask);
                if (done == readTask)
                {
                    var evt = await readTask;
                    await ctx.Response.WriteAsync(evt, ctx.RequestAborted);
                    readTask = channel.Reader.ReadAsync(ctx.RequestAborted).AsTask();
                }
                else
                {
                    await tickTask;
                    await ctx.Response.WriteAsync(": keepalive\n\n", ctx.RequestAborted);
                    tickTask = keepalive.WaitForNextTickAsync(ctx.RequestAborted).AsTask();
                }
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }
}
