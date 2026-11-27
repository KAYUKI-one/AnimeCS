using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AnimeCS.Data;

internal sealed class Rejection(string message, int status = 400) : Exception(message)
{
    internal int Status { get; } = status;
}

internal sealed class AccountDb
{
    private readonly string _connection;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    internal AccountDb(string directory)
    {
        _connection = new SqliteConnectionStringBuilder {DataSource=Path.Combine(directory,"players.db"), ForeignKeys=true}.ToString();
        using var db = Open();
        using var tx = db.BeginTransaction();
        Execute(db,tx,"""
            CREATE TABLE IF NOT EXISTS accounts(
                id TEXT PRIMARY KEY COLLATE NOCASE, salt BLOB NOT NULL, pass BLOB NOT NULL, state TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions(
                hash TEXT PRIMARY KEY, owner TEXT NOT NULL REFERENCES accounts(id), expires INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS friends(
                owner TEXT NOT NULL REFERENCES accounts(id), other TEXT NOT NULL REFERENCES accounts(id), PRIMARY KEY(owner,other));
            CREATE TABLE IF NOT EXISTS invites(
                lead TEXT NOT NULL REFERENCES accounts(id), guest TEXT NOT NULL REFERENCES accounts(id), PRIMARY KEY(lead,guest));
            CREATE TABLE IF NOT EXISTS party(
                member TEXT PRIMARY KEY REFERENCES accounts(id), lead TEXT NOT NULL REFERENCES accounts(id));
            CREATE INDEX IF NOT EXISTS party_lead ON party(lead);
            CREATE TABLE IF NOT EXISTS chat(seq INTEGER PRIMARY KEY AUTOINCREMENT, owner TEXT NOT NULL, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS battle(
                slot INTEGER PRIMARY KEY CHECK(slot=1), room TEXT NOT NULL, lead TEXT NOT NULL, seats TEXT NOT NULL, expires INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS battle_history(room TEXT PRIMARY KEY, seats TEXT NOT NULL, created INTEGER NOT NULL, settled INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS battle_progress(room TEXT NOT NULL REFERENCES battle_history(room) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL, seat INTEGER NOT NULL, occurred INTEGER NOT NULL, PRIMARY KEY(room,ordinal,seat));
            CREATE TABLE IF NOT EXISTS battle_lineups(room TEXT PRIMARY KEY REFERENCES battle_history(room) ON DELETE CASCADE, equipment TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS party_results(lead TEXT PRIMARY KEY REFERENCES accounts(id), room TEXT NOT NULL REFERENCES battle_history(room) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS battle_restarts(previous TEXT PRIMARY KEY REFERENCES battle_history(room) ON DELETE CASCADE,
                successor TEXT NOT NULL REFERENCES battle_history(room) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS daily(owner TEXT NOT NULL, day TEXT NOT NULL, task TEXT NOT NULL,
                progress INTEGER NOT NULL DEFAULT 0, claimed INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(owner,day,task));
            CREATE TABLE IF NOT EXISTS receipts(owner TEXT NOT NULL, day TEXT NOT NULL, event TEXT NOT NULL, PRIMARY KEY(owner,day,event));
            PRAGMA user_version=2;
            """);
        tx.Commit();
    }

    internal SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        return db;
    }

    internal static SqliteCommand Command(SqliteConnection db,SqliteTransaction tx,string sql,params object[] values)
    {
        var cmd=db.CreateCommand();
        cmd.Transaction=tx; cmd.CommandText=sql;
        for(int i=0;i<values.Length;i++) cmd.Parameters.AddWithValue("$p"+i,values[i]);
        return cmd;
    }

    internal static int Execute(SqliteConnection db,SqliteTransaction tx,string sql,params object[] values)
    {
        using var cmd=Command(db,tx,sql,values);
        return cmd.ExecuteNonQuery();
    }

    internal static string? Scalar(SqliteConnection db,SqliteTransaction tx,string sql,params object[] values)
    {
        using var cmd=Command(db,tx,sql,values);
        return cmd.ExecuteScalar()?.ToString();
    }

    internal static string[] Strings(SqliteConnection db,SqliteTransaction tx,string sql,params object[] values)
    {
        using var cmd=Command(db,tx,sql,values);
        using var reader=cmd.ExecuteReader();
        var result=new List<string>();
        while(reader.Read()) result.Add(reader.GetString(0));
        return result.ToArray();
    }

    internal static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    internal static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static byte[] Derive(string password,byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32);
}
