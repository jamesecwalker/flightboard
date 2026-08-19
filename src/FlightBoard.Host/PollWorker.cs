using FlightBoard.Core;
using FlightBoard.Core.Sources;

namespace FlightBoard.Host;

/// <summary>The heartbeat: poll the source, feed the engine, sleep, repeat.</summary>
public sealed class PollWorker : BackgroundService
{
    private readonly IAircraftSource _source;
    private readonly Engine _engine;
    private readonly SourceOptions _options;
    private readonly ILogger<PollWorker> _log;

    public PollWorker(IAircraftSource source, Engine engine, SourceOptions options, ILogger<PollWorker> log)
    {
        _source = source;
        _engine = engine;
        _options = options;
        _log = log;
        _engine.SourceName = source.Name;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Polling {Source} every {Seconds}s; home {Lat},{Lon}", _source.Name, _options.PollSeconds, _engine.Home.Lat, _engine.Home.Lon);
        var interval = TimeSpan.FromSeconds(Math.Max(0.5, _options.PollSeconds));
        using var timer = new PeriodicTimer(interval);
        var isReplay = _source is ReplaySource;
        do
        {
            try
            {
                var poll = await _source.PollAsync(stoppingToken);
                await _engine.TickAsync(poll, stoppingToken);
                if (isReplay && ((ReplaySource)_source).Finished)
                {
                    _log.LogInformation("Replay finished");
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Poll/tick failed");
            }
        } while (isReplay ? !stoppingToken.IsCancellationRequested : await timer.WaitForNextTickAsync(stoppingToken));
    }
}
