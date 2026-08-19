# FlightBoard

A split-flap "Solari" arrivals board for the garden room. When an aircraft on the Gatwick approach is about
to pass over the house the board click-clacks over to show the **flight number, airline and where it has come
from**, and highlights anything interesting (A380s, RAF, emergencies, first sightings, private jets…).

The physical board comes later. Today the "board" is a browser page served by the app that simulates the
split-flap tiles (with sound); later it can be a cheap tablet outside, and eventually a real board — the
core never changes, you add one display adapter.

```
 ┌────────────────────────┐
 │ HLE 21           AW169 │   flight · type
 │ SPECIALIST AVIATION SE │   airline / operator
 │                        │   FROM origin (or TO destination)
 │     * HELICOPTER *     │   interesting-plane tag (amber)
 │ LOW 1425FT          +2 │   low-altitude alert (amber) · aircraft queued behind
 └────────────────────────┘
```

## Run it

```powershell
dotnet run --project src/FlightBoard.Host
```
Open <http://localhost:5000>. Out of the box it runs the **simulator**: a pretend aircraft flies down the
26L approach over the house every 90 s (every 4th one is "interesting"), so you can see the board flip
without waiting for traffic. Press **T** in the browser to push a test flight (Shift+T for a highlighted
one), **F** for fullscreen, **H** to hide the status line, **S** to toggle sound.

### Go live

1. Copy `src/FlightBoard.Host/appsettings.Local.example.json` to `appsettings.Local.json` (git-ignored) and
   put your coordinates in `Home:Latitude/Longitude`. The checked-in default is a placeholder on the 26L
   approach near Lingfield so the simulator works for anyone.
2. In the same file set `Source:Kind` to `readsb`. Positions come from the free adsb.lol API with adsb.fi as
   failover (same JSON shape as a local dump1090/readsb receiver — an RTL-SDR dongle later is just another
   URL in `Source:Urls`). Airline and origin come from adsbdb.com / hexdb.io and are cached in SQLite.
3. Optionally set `Source:RecordTo` to `data/recordings/live-{date}.jsonl` so you can **replay** a real day's
   traffic later while tuning (`Source:Kind=replay`, `Source:ReplayFile=…`, `Source:ReplaySpeed=0` for
   instant).

Everything can also be passed on the command line, e.g.
`dotnet run --project src/FlightBoard.Host -- --Source:Kind=readsb --Home:Latitude=51.17 --Home:Longitude=-0.05`.

### Tune it while watching the sky

All the timing knobs live under `Tracker` and can be changed at runtime without a restart:

```
GET  /api/config
PUT  /api/config   {"leadSeconds": 50, "corridorMetres": 600, "maxAltitudeFt": 4500}
GET  /api/state    every tracked aircraft with tCpa / dCpa / altitude / why it was rejected
GET  /api/history  what has been on the board
POST /api/simulate {"flight":"EZY8123","airline":"easyJet","origin":"Alicante","tag":"A380","score":50}
POST /api/clear
```

| Setting | Default | Meaning |
|---|---|---|
| `LeadSeconds` | 45 | Start flipping this many seconds before the aircraft is overhead (the flip itself takes ~3 s). |
| `MinElevationDeg` | 20 | An aircraft is "overhead" if at its closest point it will be at least this far above the horizon. 20° catches arrivals being vectored 3–4 km away at 4–6,000 ft (what you hear and see); 30°+ means nearly straight above. |
| `CorridorMetres` / `MaxCorridorMetres` | 800 / 5000 | Floor and cap on the lateral corridor derived from the elevation angle. |
| `HoldSeconds` | 20 | Keep it on the board this long after it has passed. |
| `MinDisplaySeconds` | 15 | Never swap the board faster than this. |
| `Approach.MaxAltitudeFt` | 9000 | Anything higher is an overflight. |
| `Approach.MaxClimbFpm` | 200 | Anything climbing faster is a departure. Departures held *level* at 6–7,000 ft still pass; they are recognised from the route (origin = Gatwick) and shown as "TO …" when `Board:ShowDepartures` is on, otherwise skipped. |
| `Approach.Headings` | empty | Restrict to runway tracks (Gatwick 26L = 258, 08R = 78, ±25°). Only useful if the house is near the extended centreline; 25 km out from the runway arrivals are still being vectored and come from every direction. |

Run it live for a while with recording on, look at `/api/state` while planes go over, then lock the
values in `appsettings.json`.

**What the traffic looks like ~25 km east of Gatwick** (from the first morning's recording at the author's house): Gatwick arrivals pass 2.5–4.5 km
from the house at 4,500–7,000 ft descending, on headings anywhere from 350° to 080° (being vectored round to final), one
every 2–4 minutes in the morning peak. Only ~1 in 7 comes inside a fixed 1 km corridor, which is why "overhead" is
defined by elevation angle rather than distance. Departures get held level at 6–7,000 ft and can pass over too.

## What counts as interesting

Rules live in `src/FlightBoard.Core/Interest/Rules/Rules.cs`; lists are configurable under `Interest`.
Highest score wins the label on the board; score ≥ 50 also gets the amber accent.

| Score | Tag | Trigger |
|---|---|---|
| 100 | EMERGENCY / RADIO FAILURE / HIJACK | squawk 7700 / 7600 / 7500 |
| 85 | *your label* | `Interest:WatchRegistrations` / `WatchHexes` |
| 80 | RAF, USAF, GERMAN AF… | military callsign prefixes, Mode-S hex ranges, UK serial-style regs, readsb military flag |
| 55–62 | NEW AIRLINE / NEW TYPE / NEW ROUTE / FIRST VISIT | never seen at the house before (after a 7-day warm-up) |
| 50 | A380, 747, HERCULES, HELICOPTER… | `Interest:UnusualTypes` / `UnusualCategories` |
| 45 | GO AROUND | was on the approach, then climbed away below 3,000 ft (re-arms for the second attempt) |
| 30 | PRIVATE JET | bizjet type, small emitter category, no scheduled route |
| 20 | LONG HAUL | origin more than 8,000 km away |
| 35 | DIVERTED | off by default (`DetectDiversions`) — the route DBs only hold one leg of multi-stop flights |

## How it works

```
 IAircraftSource ──poll──▶ Tracker ──▶ BoardScheduler ──▶ Engine ──▶ IBoardDisplay(s)
 (adsb.lol / adsb.fi /     per-hex CPA    what's on the      enrich      web sim (SSE)
  local readsb /           + approach     board right now    + rules     console
  simulator / replay)      filter + state machine            + sightings ── later: Vestaboard, serial split-flap…
```

* **Tracker** (`Core/Tracking/Tracker.cs`) — for every aircraft, dead-reckons the last position by its
  age, computes the straight-line closest point of approach to home (`Geo/Cpa.cs`), filters out
  overflights/departures, and runs `Idle → Approaching → Overhead → Passed → Cooldown`. Pure; driven by
  the poll timestamp, so recordings replay identically.
* **BoardScheduler** — arrivals come 90–120 s apart; it never pre-empts a shown flight with a later one,
  but shows the next the moment the current one is gone (or goes idle: clock + "NEXT BA 2723 IN 3 MIN").
* **Displays** — the engine produces a `BoardMessage` (flight, airline, origin, tag…); each
  `IBoardDisplay` declares `BoardCapabilities` (rows, cols, charset, colour) and `FrameLayout` renders the
  message to fit: upper-casing, transliteration (Málaga → MALAGA), truncation, 1/2/3/4+ row layouts.
  The browser sim and the console are the two adapters today. A physical board = one new class.

### Adding a physical board

Implement `IBoardDisplay` (see `Core/Board/ConsoleDisplay.cs` — it is ~25 lines), register it in
`Host/Program.cs` next to the others. Set `Board:Rows/Cols/Charset` to match the hardware; if the board
has no `*`, the tag decoration falls back automatically; if it has no colour, `Accent` is simply omitted.

## Project layout

```
src/FlightBoard.Core    engine: sources, tracking, enrichment, interest rules, board rendering, storage
src/FlightBoard.Host    ASP.NET Core host: poll loop, SSE, API, wwwroot split-flap sim
tests/FlightBoard.Tests xUnit: CPA maths, tracker scenarios, layouts, rules, sources
fixtures/               sample recording (a real BA 777 on the 26L approach) for replay tests
data/                   runtime: SQLite DB + live recordings (git-ignored)
```

## Raspberry Pi

```powershell
dotnet publish src/FlightBoard.Host -c Release -r linux-arm64 --self-contained -o publish/linux-arm64
```
Copy `publish/linux-arm64` to the Pi, `chmod +x FlightBoard.Host`, run it (listens on `:5000`), point a
browser/tablet at it. A systemd unit is the usual way to keep it running:

```
[Unit]
Description=FlightBoard
After=network-online.target
[Service]
WorkingDirectory=/home/pi/flightboard
ExecStart=/home/pi/flightboard/FlightBoard.Host --Source:Kind=readsb
Restart=always
[Install]
WantedBy=multi-user.target
```

## Data sources (all free, no keys)

* Positions: `https://api.adsb.lol/v2/lat/{lat}/lon/{lon}/dist/{nm}`, `https://opendata.adsb.fi/api/v2/…`
  (~1 request/s is polite; we poll every 2 s).
* Routes/airlines: `https://api.adsbdb.com/v0/callsign/{callsign}`, aircraft `…/aircraft/{hex}`;
  fallback `https://hexdb.io/api/v1/route/icao/{callsign}`. Cached 7 days (misses 12 h).
* Offline fallback: an ICAO-prefix → airline table in `Core/Enrichment/AirlinePrefixTable.cs`.
