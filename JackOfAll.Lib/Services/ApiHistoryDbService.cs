using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace JackOfAll.Lib.Services;

public static class ApiHistoryDbService
{
    private static readonly string DbPath = Path.Combine(AppPaths.Root, "api-history.db");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static void EnsureDatabase()
    {
        AppPaths.EnsureRoot();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS history (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                workspace     TEXT NOT NULL,
                request_name  TEXT NOT NULL,
                timestamp     TEXT NOT NULL,
                status_code   INTEGER NOT NULL,
                elapsed_ms    INTEGER NOT NULL,
                request_json  TEXT NOT NULL,
                response_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_history_lookup
                ON history (workspace, request_name, timestamp DESC);
            """;
        cmd.ExecuteNonQuery();

        var oldHistory = Path.Combine(AppPaths.Root, "request-history.json");
        if (File.Exists(oldHistory))
            File.Delete(oldHistory);
    }

    public static long Add(string workspace, string requestName, ApiRequest request, ApiResponse response)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO history (workspace, request_name, timestamp, status_code, elapsed_ms, request_json, response_json)
            VALUES ($ws, $name, $ts, $status, $elapsed, $req, $res);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$ws", workspace);
        cmd.Parameters.AddWithValue("$name", requestName);
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$status", response.StatusCode);
        cmd.Parameters.AddWithValue("$elapsed", response.ElapsedMs);
        cmd.Parameters.AddWithValue("$req", JsonSerializer.Serialize(request, JsonOpts));
        cmd.Parameters.AddWithValue("$res", JsonSerializer.Serialize(response, JsonOpts));
        return (long)cmd.ExecuteScalar()!;
    }

    public static List<ApiHistoryEntry> LoadForRequest(string workspace, string requestName, int limit = 100)
    {
        var entries = new List<ApiHistoryEntry>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, workspace, request_name, timestamp, status_code, elapsed_ms, request_json, response_json
            FROM history
            WHERE workspace = $ws AND request_name = $name
            ORDER BY timestamp DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$ws", workspace);
        cmd.Parameters.AddWithValue("$name", requestName);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new ApiHistoryEntry
            {
                Id = reader.GetInt64(0),
                Workspace = reader.GetString(1),
                RequestName = reader.GetString(2),
                Timestamp = DateTime.Parse(reader.GetString(3)),
                StatusCode = reader.GetInt32(4),
                ElapsedMs = reader.GetInt64(5),
                RequestJson = reader.GetString(6),
                ResponseJson = reader.GetString(7)
            });
        }
        return entries;
    }

    public static void DeleteForRequest(string workspace, string requestName)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM history WHERE workspace = $ws AND request_name = $name;";
        cmd.Parameters.AddWithValue("$ws", workspace);
        cmd.Parameters.AddWithValue("$name", requestName);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteForWorkspace(string workspace)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM history WHERE workspace = $ws;";
        cmd.Parameters.AddWithValue("$ws", workspace);
        cmd.ExecuteNonQuery();
    }

    public static int CountForRequest(string workspace, string requestName)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM history WHERE workspace = $ws AND request_name = $name;";
        cmd.Parameters.AddWithValue("$ws", workspace);
        cmd.Parameters.AddWithValue("$name", requestName);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }
}
