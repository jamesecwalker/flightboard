using System.Text.Json;
using System.Text.Json.Serialization;
using FlightBoard.Core;
using FlightBoard.Core.Board;
using FlightBoard.Core.Enrichment;
using FlightBoard.Core.Geo;
using FlightBoard.Core.Interest;
using FlightBoard.Core.Sources;
using FlightBoard.Core.Storage;
using FlightBoard.Core.Tracking;
using FlightBoard.Host;

var builder = WebApplication.CreateBuilder(args);
// Personal settings (home coordinates, live source) live in appsettings.Local.json, which is git-ignored.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
builder.Configuration.AddCommandLine(args); // command line still wins over everything
var cfg = builder.Configuration;

// ---- options (plain POCOs bound once; TrackerOptions is mutable at runtime via PUT /api/config) ----
var home = new GeoPoint(cfg.GetValue("Home:Latitude", 51.171), cfg.GetValue("Home:Longitude", -0.051));
var sourceOptions = cfg.GetSection("Source").Get<SourceOptions>() ?? new SourceOptions();
var trackerOptions = cfg.GetSection("Tracker").Get<TrackerOptions>() ?? new TrackerOptions();
var boardOptions = cfg.GetSection("Board").Get<BoardOptions>() ?? new BoardOptions();
var interestOptions = cfg.GetSection("Interest").Get<InterestOptions>() ?? new InterestOptions();
var enrichmentOptions = cfg.GetSection("Enrichment").Get<EnrichmentOptions>() ?? new EnrichmentOptions();
var quietOptions = cfg.GetSection("QuietHours").Get<QuietHoursOptions>() ?? new QuietHoursOptions();
var dbPath = cfg.GetValue("Storage:Path", "data/flightboard.db")!;
var consoleDisplay = cfg.GetValue("Displays:Console", true);
// Configuration binding *appends* to list defaults, so de-duplicate the ones that have defaults in code.
sourceOptions.Urls = sourceOptions.Urls.Distinct().ToList();
trackerOptions.Approach.Headings = trackerOptions.Approach.Headings.Distinct().ToList();

builder.Services.AddSingleton(sourceOptions);
builder.Services.AddSingleton(trackerOptions);
builder.Services.AddSingleton(boardOptions);
builder.Services.AddSingleton(interestOptions);
builder.Services.AddSingleton(enrichmentOptions);
builder.Services.AddSingleton(quietOptions);

builder.Services.AddHttpClient("lookups", c =>
{
    c.Timeout = TimeSpan.FromSeconds(8);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("FlightBoard/0.1 (+https://github.com/jimw/flightboard; hobby split-flap board)");
});
builder.Services.AddHttpClient("source", c =>
{
    c.Timeout = TimeSpan.FromSeconds(6);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("FlightBoard/0.1 (hobby split-flap board)");
});

builder.Services.AddSingleton(new Db(dbPath));
builder.Services.AddSingleton<SightingsRepo>();
builder.Services.AddSingleton(sp => new AdsbdbClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("lookups"), enrichmentOptions, sp.GetRequiredService<ILogger<AdsbdbClient>>()));
builder.Services.AddSingleton(sp => new HexdbClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("lookups"), enrichmentOptions, sp.GetRequiredService<ILogger<HexdbClient>>()));
builder.Services.AddSingleton<IFlightEnricher, CachedEnricher>();
builder.Services.AddSingleton(new InterestEvaluator(InterestEvaluator.DefaultRules()));

// ---- displays: add a physical board here later; nothing else changes ----
var caps = boardOptions.ToCapabilities();
var web = new WebDisplay(caps);
builder.Services.AddSingleton(web);
builder.Services.AddSingleton<IBoardDisplay>(web);
if (consoleDisplay) builder.Services.AddSingleton<IBoardDisplay>(new ConsoleDisplay(caps));
builder.Services.AddSingleton<CompositeDisplay>();

// ---- source ----
builder.Services.AddSingleton<IAircraftSource>(sp =>
{
    var lf = sp.GetRequiredService<ILoggerFactory>();
    IAircraftSource inner = sourceOptions.Kind.ToLowerInvariant() switch
    {
        "readsb" or "live" => new ReadsbJsonSource(sp.GetRequiredService<IHttpClientFactory>().CreateClient("source"), sourceOptions, home, lf.CreateLogger<ReadsbJsonSource>()),
        "replay" => new ReplaySource(sourceOptions.ReplayFile ?? throw new InvalidOperationException("Source:ReplayFile is required for replay"), sourceOptions.ReplaySpeed),
        _ => new SimulatedSource(sourceOptions.Simulator, home),
    };
    return string.IsNullOrWhiteSpace(sourceOptions.RecordTo) ? inner : new RecordingSource(inner, sourceOptions.RecordTo);
});

builder.Services.AddSingleton(sp => new Engine(
    home, () => trackerOptions, boardOptions, interestOptions, quietOptions,
    sp.GetRequiredService<IFlightEnricher>(), sp.GetRequiredService<InterestEvaluator>(),
    sp.GetRequiredService<CompositeDisplay>(), sp.GetRequiredService<SightingsRepo>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddHostedService<PollWorker>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/stream", (WebDisplay d, HttpContext ctx) => d.StreamAsync(ctx));

api.MapGet("/state", (Engine e) =>
{
    var s = e.State;
    return Results.Ok(new
    {
        s.LastPollAt, s.TrackedCount, s.SourceName, s.Quiet, s.Current,
        home = new { e.Home.Lat, e.Home.Lon },
        flights = s.Flights.Select(f => new
        {
            f.Hex, f.Callsign, f.Registration, f.Type, f.Phase, f.Squawk,
            tCpa = Num(f.TimeToCpaSeconds), dCpa = Num(f.CpaDistanceMetres), dNow = Num(f.DistanceNowMetres),
            corridor = Num(f.CorridorMetres), elev = Num(f.ElevationAtCpaDeg),
            alt = f.LastSample?.AltBaroFt, gs = f.LastSample?.GroundSpeedKt, track = f.LastSample?.TrackDeg, rate = f.LastSample?.BaroRateFpm,
            lat = f.LastSample?.Lat, lon = f.LastSample?.Lon, reject = f.LastRejectReason, f.WentAround,
        }),
    });
});

api.MapGet("/history", (SightingsRepo repo, int? limit) => Results.Ok(repo.Recent(Math.Clamp(limit ?? 50, 1, 500))));

api.MapPost("/history/{id:long}/replay", async (long id, SightingsRepo repo, Engine e, CancellationToken ct) =>
{
    var s = repo.Get(id);
    if (s is null) return Results.NotFound();
    await e.ReplaySightingAsync(s, ct);
    return Results.Ok(e.Current);
});

api.MapGet("/config", (TrackerOptions t, BoardOptions b, SourceOptions s) => Results.Ok(new { tracker = t, board = b, source = new { s.Kind, s.Urls, s.RadiusNm, s.PollSeconds, s.RecordTo } }));

api.MapPut("/config", (TrackerOptions current, TrackerPatch patch) =>
{
    if (patch.LeadSeconds is { } a) current.LeadSeconds = a;
    if (patch.CorridorMetres is { } b) current.CorridorMetres = b;
    if (patch.MinElevationDeg is { } b2) current.MinElevationDeg = b2;
    if (patch.MaxCorridorMetres is { } b3) current.MaxCorridorMetres = b3;
    if (patch.HoldSeconds is { } c) current.HoldSeconds = c;
    if (patch.MinDisplaySeconds is { } d) current.MinDisplaySeconds = d;
    if (patch.CooldownSeconds is { } e) current.CooldownSeconds = e;
    if (patch.ConfirmPolls is { } f) current.ConfirmPolls = f;
    if (patch.PrefetchSeconds is { } g) current.PrefetchSeconds = g;
    if (patch.IdleRefreshSeconds is { } h) current.IdleRefreshSeconds = h;
    if (patch.ApproachEnabled is { } i) current.Approach.Enabled = i;
    if (patch.MaxAltitudeFt is { } j) current.Approach.MaxAltitudeFt = j;
    if (patch.MaxClimbFpm is { } k) current.Approach.MaxClimbFpm = k;
    if (patch.Headings is { } l) current.Approach.Headings = l;
    if (patch.HeadingToleranceDeg is { } m) current.Approach.HeadingToleranceDeg = m;
    return Results.Ok(current);
});

api.MapPost("/simulate", async (Engine e, SimulateRequest r, CancellationToken ct) =>
{
    await e.SimulateAsync(r.Flight ?? "EZY 8123", r.Airline ?? "easyJet", r.Origin ?? "Alicante", r.Type, r.Tag, r.Score ?? 50, r.Attract ?? false, ct);
    return Results.Ok(e.Current);
});

api.MapPost("/clear", async (Engine e, CancellationToken ct) => { await e.ClearAsync(ct); return Results.Ok(); });

api.MapGet("/health", (Engine e, WebDisplay d) => Results.Ok(new { ok = true, e.LastPollAt, clients = d.ClientCount }));

app.Run();

static double? Num(double v) => double.IsNaN(v) || double.IsInfinity(v) ? null : Math.Round(v, 1);

record TrackerPatch(
    double? LeadSeconds, double? CorridorMetres, double? MinElevationDeg, double? MaxCorridorMetres, double? HoldSeconds, double? MinDisplaySeconds, double? CooldownSeconds,
    int? ConfirmPolls, double? PrefetchSeconds, double? IdleRefreshSeconds,
    bool? ApproachEnabled, int? MaxAltitudeFt, int? MaxClimbFpm, List<double>? Headings, double? HeadingToleranceDeg);

record SimulateRequest(string? Flight, string? Airline, string? Origin, string? Type, string? Tag, int? Score, bool? Attract);
