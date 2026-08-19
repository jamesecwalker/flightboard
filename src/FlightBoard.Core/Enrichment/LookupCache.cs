using System.Text.Json;
using Dapper;
using FlightBoard.Core.Storage;

namespace FlightBoard.Core.Enrichment;

/// <summary>SQLite-backed key→JSON cache with separate TTLs for hits and misses. One instance per table.</summary>
public sealed class LookupCache<T> where T : class
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Db _db;
    private readonly string _table;
    private readonly string _keyCol;
    private readonly TimeSpan _hitTtl;
    private readonly TimeSpan _missTtl;

    public LookupCache(Db db, string table, string keyColumn, TimeSpan hitTtl, TimeSpan missTtl)
    {
        _db = db;
        _table = table;
        _keyCol = keyColumn;
        _hitTtl = hitTtl;
        _missTtl = missTtl;
    }

    public sealed record Entry(T? Value, bool NotFound);

    /// <summary>Returns null when there is no fresh entry (so the caller should go and look).</summary>
    public Entry? Get(string key)
    {
        using var c = _db.Open();
        var row = c.QuerySingleOrDefault<(string? Json, long NotFound, string FetchedAt)>(
            $"SELECT Json, NotFound, FetchedAt FROM {_table} WHERE {_keyCol} = @key", new { key });
        if (row.FetchedAt is null) return null;
        var fetched = DateTimeOffset.Parse(row.FetchedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var age = DateTimeOffset.UtcNow - fetched;
        if (row.NotFound != 0) return age < _missTtl ? new Entry(null, true) : null;
        if (age >= _hitTtl || row.Json is null) return null;
        var value = JsonSerializer.Deserialize<T>(row.Json, Json);
        return value is null ? null : new Entry(value, false);
    }

    public void Put(string key, T? value)
    {
        using var c = _db.Open();
        c.Execute($"""
            INSERT INTO {_table}({_keyCol}, Json, NotFound, FetchedAt) VALUES (@key, @json, @notFound, @at)
            ON CONFLICT({_keyCol}) DO UPDATE SET Json = excluded.Json, NotFound = excluded.NotFound, FetchedAt = excluded.FetchedAt
            """, new
        {
            key,
            json = value is null ? null : JsonSerializer.Serialize(value, Json),
            notFound = value is null ? 1 : 0,
            at = DateTimeOffset.UtcNow.ToString("O"),
        });
    }
}
