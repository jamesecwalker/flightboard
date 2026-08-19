using Dapper;
using Microsoft.Data.Sqlite;

namespace FlightBoard.Core.Storage;

/// <summary>Owns the SQLite file and its schema. Tiny on purpose: two caches and a sightings log.</summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = full, Cache = SqliteCacheMode.Shared }.ToString();
        Path_ = full;
        EnsureSchema();
    }

    public string Path_ { get; }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void EnsureSchema()
    {
        using var c = Open();
        c.Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS RouteCache (
                Callsign   TEXT PRIMARY KEY,
                Json       TEXT,
                NotFound   INTEGER NOT NULL DEFAULT 0,
                FetchedAt  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS AircraftCache (
                Hex        TEXT PRIMARY KEY,
                Json       TEXT,
                NotFound   INTEGER NOT NULL DEFAULT 0,
                FetchedAt  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Sightings (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Hex          TEXT NOT NULL,
                Callsign     TEXT,
                Flight       TEXT,
                Registration TEXT,
                Type         TEXT,
                AirlineIcao  TEXT,
                AirlineName  TEXT,
                OriginIcao   TEXT,
                OriginName   TEXT,
                Tags         TEXT,
                AltFt        INTEGER,
                SeenAt       TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Sightings_SeenAt ON Sightings(SeenAt);
            CREATE INDEX IF NOT EXISTS IX_Sightings_Hex ON Sightings(Hex);
            CREATE TABLE IF NOT EXISTS Meta (
                Key   TEXT PRIMARY KEY,
                Value TEXT
            );
            """);
        c.Execute("INSERT OR IGNORE INTO Meta(Key, Value) VALUES ('FirstRunAt', @now)", new { now = DateTimeOffset.UtcNow.ToString("O") });
        // Additive migrations: SQLite has no ADD COLUMN IF NOT EXISTS, so probe first.
        var cols = c.Query<string>("SELECT name FROM pragma_table_info('Sightings')").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!cols.Contains("IsDeparture")) c.Execute("ALTER TABLE Sightings ADD COLUMN IsDeparture INTEGER NOT NULL DEFAULT 0");
    }

    public DateTimeOffset FirstRunAt
    {
        get
        {
            using var c = Open();
            var v = c.ExecuteScalar<string?>("SELECT Value FROM Meta WHERE Key='FirstRunAt'");
            return v is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }
    }
}
