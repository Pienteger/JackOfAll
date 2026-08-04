using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using JackOfAll.Lib.Settings;

namespace JackOfAll.Lib.Services;

public sealed record ApiHistoryWriteResult(bool Stored, string? Notice = null);

public static class ApiHistoryDbService
{
    public const int DefaultMaxEntriesPerRequest = 50;
    public const int DefaultHistoryStorageLimitMb = 100;

    private static int MaxEntriesPerRequest =>
        Math.Clamp(AppSettings.Current?.ApiHistoryEntriesPerRequest ?? DefaultMaxEntriesPerRequest, 10, 200);

    private static long MaxLogicalPayloadBytes =>
        (long)Math.Clamp(AppSettings.Current?.ApiHistoryStorageLimitMb ?? DefaultHistoryStorageLimitMb, 10, 500)
        * 1024 * 1024;

    private static readonly string DbPath = Path.Combine(AppPaths.Root, "api-history.db");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static void EnsureDatabase()
    {
        AppPaths.EnsureRoot();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                workspace TEXT NOT NULL,
                request_name TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                status_code INTEGER NOT NULL,
                elapsed_ms INTEGER NOT NULL,
                request_json TEXT NOT NULL,
                response_json TEXT NOT NULL,
                payload_bytes INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_history_lookup
                ON history (workspace, request_name, timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_history_oldest ON history (timestamp ASC);
            """;
        cmd.ExecuteNonQuery();
        EnsurePayloadBytesColumn(conn);

        var oldHistory = Path.Combine(AppPaths.Root, "request-history.json");
        if (File.Exists(oldHistory)) File.Delete(oldHistory);
    }

    public static Task<ApiHistoryWriteResult> AddAsync(
        string workspace, string requestName, ApiRequest request, ApiResponse response) =>
        Task.Run(() => AddBounded(workspace, requestName, request, response));

    public static long Add(string workspace, string requestName, ApiRequest request, ApiResponse response) =>
        AddBounded(workspace, requestName, request, response).Stored ? 1 : 0;

    private static ApiHistoryWriteResult AddBounded(
        string workspace, string requestName, ApiRequest request, ApiResponse response)
    {
        var requestJson = JsonSerializer.Serialize(request, JsonOpts);
        var responseJson = JsonSerializer.Serialize(response, JsonOpts);
        var payloadBytes = Encoding.UTF8.GetByteCount(requestJson) + Encoding.UTF8.GetByteCount(responseJson);
        if (payloadBytes > MaxLogicalPayloadBytes)
            return new(false, $"Response was not added to history because it exceeds the {MaxLogicalPayloadBytes / 1024 / 1024} MB history limit.");

        using var conn = Open();
        using var transaction = conn.BeginTransaction();
        Execute(conn, transaction, """
            DELETE FROM history WHERE id IN (
                SELECT id FROM history
                WHERE workspace = $ws AND request_name = $name
                ORDER BY timestamp DESC LIMIT -1 OFFSET $keep
            );
            """, ("$ws", workspace), ("$name", requestName), ("$keep", MaxEntriesPerRequest - 1));

        var currentBytes = ScalarLong(conn, transaction, "SELECT COALESCE(SUM(payload_bytes), 0) FROM history;");
        while (currentBytes + payloadBytes > MaxLogicalPayloadBytes)
        {
            var removed = Execute(conn, transaction, """
                DELETE FROM history WHERE id IN (
                    SELECT id FROM history ORDER BY timestamp ASC LIMIT 25
                );
                """);
            if (removed == 0)
                return new(false, $"Response was not added to history because the {MaxLogicalPayloadBytes / 1024 / 1024} MB history limit is full.");
            currentBytes = ScalarLong(conn, transaction, "SELECT COALESCE(SUM(payload_bytes), 0) FROM history;");
        }

        Execute(conn, transaction, """
            INSERT INTO history
                (workspace, request_name, timestamp, status_code, elapsed_ms, request_json, response_json, payload_bytes)
            VALUES ($ws, $name, $ts, $status, $elapsed, $req, $res, $bytes);
            """, ("$ws", workspace), ("$name", requestName), ("$ts", DateTime.UtcNow.ToString("o")),
            ("$status", response.StatusCode), ("$elapsed", response.ElapsedMs),
            ("$req", requestJson), ("$res", responseJson), ("$bytes", payloadBytes));
        transaction.Commit();
        return new(true);
    }

    public static List<ApiHistoryEntry> LoadForRequest(
        string workspace, string requestName, int? limit = null)
    {
        var entries = new List<ApiHistoryEntry>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace, request_name, timestamp, status_code, elapsed_ms
            FROM history WHERE workspace = $ws AND request_name = $name
            ORDER BY timestamp DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$ws", workspace);
        cmd.Parameters.AddWithValue("$name", requestName);
        cmd.Parameters.AddWithValue("$limit", limit ?? MaxEntriesPerRequest);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            entries.Add(new ApiHistoryEntry
            {
                Id = reader.GetInt64(0),
                Workspace = reader.GetString(1),
                RequestName = reader.GetString(2),
                Timestamp = DateTime.Parse(reader.GetString(3)),
                StatusCode = reader.GetInt32(4),
                ElapsedMs = reader.GetInt64(5)
            });
        return entries;
    }

    public static ApiHistoryEntry? LoadEntry(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace, request_name, timestamp, status_code, elapsed_ms, request_json, response_json
            FROM history WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ApiHistoryEntry
        {
            Id = reader.GetInt64(0),
            Workspace = reader.GetString(1),
            RequestName = reader.GetString(2),
            Timestamp = DateTime.Parse(reader.GetString(3)),
            StatusCode = reader.GetInt32(4),
            ElapsedMs = reader.GetInt64(5),
            RequestJson = reader.GetString(6),
            ResponseJson = reader.GetString(7)
        };
    }

    public static void DeleteForRequest(string workspace, string requestName) =>
        ExecuteSimple("DELETE FROM history WHERE workspace = $ws AND request_name = $name;",
            ("$ws", workspace), ("$name", requestName));

    public static void RenameRequest(string workspace, string oldName, string newName) =>
        ExecuteSimple("UPDATE history SET request_name = $new WHERE workspace = $ws AND request_name = $old;",
            ("$new", newName), ("$ws", workspace), ("$old", oldName));

    public static void DeleteForWorkspace(string workspace) =>
        ExecuteSimple("DELETE FROM history WHERE workspace = $ws;", ("$ws", workspace));

    public static int CountForRequest(string workspace, string requestName)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM history WHERE workspace = $ws AND request_name = $name;";
        cmd.Parameters.AddWithValue("$ws", workspace);
        cmd.Parameters.AddWithValue("$name", requestName);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void EnsurePayloadBytesColumn(SqliteConnection conn)
    {
        using var info = conn.CreateCommand();
        info.CommandText = "PRAGMA table_info(history);";
        using var reader = info.ExecuteReader();
        var exists = false;
        while (reader.Read()) exists |= reader.GetString(1) == "payload_bytes";
        reader.Close();
        if (!exists)
            Execute(conn, null, "ALTER TABLE history ADD COLUMN payload_bytes INTEGER NOT NULL DEFAULT 0;");
        Execute(conn, null,
            "UPDATE history SET payload_bytes = length(CAST(request_json AS BLOB)) + length(CAST(response_json AS BLOB)) WHERE payload_bytes = 0;");
    }

    private static void ExecuteSimple(string sql, params (string Name, object Value)[] parameters)
    {
        using var conn = Open();
        Execute(conn, null, sql, parameters);
    }

    private static int Execute(
        SqliteConnection conn, SqliteTransaction? tx, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var parameter in parameters)
            cmd.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return cmd.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }
}
