using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace OnlyFate.Database;

public sealed record DatabaseVerification(string DatabasePath, int SchemaVersion, string SqliteVersion,
    IReadOnlyList<string> Tables, IReadOnlyList<string> LogicalReferenceErrors);

public static class DatabaseSchema
{
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnlyFate", "Data", "OnlyFate.db");
    private const int Version = 1;
    private const int ApplicationId = 0x4F464154; // OFAT
    private const string ScriptName = "001_Initial.sql";
    private static readonly string[] ExpectedTables = ["CaseClarification", "CaseRevision", "ExecutionCall", "NamingCase", "NamingReport", "ProcessingRun", "ProcessingStep", "ReportArtifact", "SchemaMigration"];

    public static DatabaseVerification Initialize(string databasePath)
    {
        string path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (SqliteConnection db = Open(path, SqliteOpenMode.ReadWriteCreate))
        using (SqliteTransaction transaction = db.BeginTransaction(deferred: false))
        {
            int version = Convert.ToInt32(Scalar(db, transaction, "PRAGMA user_version"));
            int applicationId = Convert.ToInt32(Scalar(db, transaction, "PRAGMA application_id"));
            if (version == 0)
            {
                if (applicationId != 0 || GetTables(db, transaction).Count != 0)
                {
                    throw new InvalidOperationException("檔案不是空白資料庫，拒絕套用 OnlyFate schema。");
                }
                Execute(db, transaction, Resource("Migrations." + ScriptName));
                using SqliteCommand record = db.CreateCommand();
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO SchemaMigration (SchemaMigrationID, ScriptName, ScriptSha256) VALUES ($version, $name, $hash)";
                record.Parameters.AddWithValue("$version", Version);
                record.Parameters.AddWithValue("$name", ScriptName);
                record.Parameters.AddWithValue("$hash", ScriptHash());
                record.ExecuteNonQuery();
                Execute(db, transaction, $"PRAGMA user_version={Version}; PRAGMA application_id={ApplicationId}");
            }
            else
            {
                CheckIdentity(db, transaction, version, applicationId);
            }
            transaction.Commit();
        }
        return Verify(path);
    }

    public static DatabaseVerification Verify(string databasePath)
    {
        string path = Path.GetFullPath(databasePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("資料庫尚未建立。", path);
        }
        using SqliteConnection db = Open(path, SqliteOpenMode.ReadOnly);
        using SqliteTransaction transaction = db.BeginTransaction(deferred: true);
        int version = Convert.ToInt32(Scalar(db, transaction, "PRAGMA user_version"));
        CheckIdentity(db, transaction, version, Convert.ToInt32(Scalar(db, transaction, "PRAGMA application_id")));
        List<string> tables = GetTables(db, transaction);
        if (!tables.SequenceEqual(ExpectedTables))
        {
            throw new InvalidOperationException("資料表結構與目前 schema 版本不符。");
        }
        List<string> integrity = ReadStrings(db, transaction, "PRAGMA integrity_check");
        if (integrity.Count != 1 || integrity[0] != "ok")
        {
            throw new InvalidOperationException("SQLite integrity_check 未通過。");
        }
        List<string> logicalErrors = ReadStrings(db, transaction, Resource("Checks.logical-references.sql"));
        string sqliteVersion = (string)Scalar(db, transaction, "SELECT sqlite_version()");
        transaction.Commit();
        return new(path, version, sqliteVersion, tables, logicalErrors);
    }

    private static void CheckIdentity(SqliteConnection db, SqliteTransaction transaction, int version, int applicationId)
    {
        if (version != Version || applicationId != ApplicationId)
        {
            throw new InvalidOperationException("不是支援的 OnlyFate schema 版本；不會覆寫或降版。");
        }
        using SqliteCommand command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ScriptName, ScriptSha256 FROM SchemaMigration WHERE SchemaMigrationID=$version";
        command.Parameters.AddWithValue("$version", Version);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1)
            || reader.GetString(0) != ScriptName || reader.GetString(1) != ScriptHash())
        {
            throw new InvalidOperationException("Migration 腳本與已套用紀錄不一致，拒絕重新套用。");
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        SqliteConnection db = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        try
        {
            db.Open();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private static string Resource(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("缺少內嵌 SQL 資源：" + name);
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    private static string ScriptHash() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Resource("Migrations." + ScriptName))));

    private static List<string> GetTables(SqliteConnection db, SqliteTransaction transaction) =>
        ReadStrings(db, transaction, "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

    private static object Scalar(SqliteConnection db, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteScalar()!;
    }

    private static void Execute(SqliteConnection db, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> ReadStrings(SqliteConnection db, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<string> values = [];
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }
        return values;
    }
}
