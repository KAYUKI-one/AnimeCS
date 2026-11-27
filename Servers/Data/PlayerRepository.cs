using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AnimeCS.Data;

internal sealed class PlayerRepository
{
    private readonly string _connection;
    internal PlayerRepository(string directory)
    {
        Directory.CreateDirectory(directory);
        _connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "players.db") }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL";
        command.ExecuteScalar();
        using var transaction = db.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        long version = (long)command.ExecuteScalar()!;
        if (version > 2) throw new InvalidDataException($"Unsupported player schema {version}");
        if (version == 0)
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS players (
                    id TEXT PRIMARY KEY, revision INTEGER NOT NULL, profile TEXT NOT NULL
                );
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        return db;
    }

    internal PlayerRecord? Read(string id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT revision, profile FROM players WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var row = cmd.ExecuteReader();
        return row.Read() ? new PlayerRecord(id, row.GetInt64(0), JsonSerializer.Deserialize<PlayerProfile>(row.GetString(1))!) : null;
    }

    internal bool TryWrite(string id, PlayerProfile body, long revision)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = revision == 0
            ? "INSERT INTO players(id,revision,profile) SELECT $id,1,$profile WHERE NOT EXISTS (SELECT 1 FROM accounts WHERE id=$id) ON CONFLICT(id) DO NOTHING"
            : "UPDATE players SET revision=revision+1,profile=$profile WHERE id=$id AND revision=$revision";
        if (revision != 0) cmd.Parameters.AddWithValue("$revision", revision);
        cmd.Parameters.AddWithValue("$profile", JsonSerializer.Serialize(body));
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    internal static string Tag(long revision) => $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";
    internal static bool TryRevision(string text, out long revision)
    {
        revision = 0;
        return text.Length > 2 && text[0] == '"' && text[^1] == '"' &&
            long.TryParse(text.AsSpan(1,text.Length-2), NumberStyles.None, CultureInfo.InvariantCulture, out revision) &&
            revision > 0 && revision < long.MaxValue;
    }
}
