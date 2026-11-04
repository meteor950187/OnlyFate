using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OnlyFate.Database;

public sealed class CaseRepository
{
    private readonly string databasePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public CaseRepository(string databasePath)
    {
        this.databasePath = Path.GetFullPath(databasePath);
        DatabaseVerification check = DatabaseSchema.Initialize(this.databasePath);
        if (check.LogicalReferenceErrors.Count != 0) throw new InvalidDataException("資料庫有不完整的案件關聯，請先執行資料庫查核。");
    }

    public IReadOnlyList<CaseSummary> List()
    {
        using SqliteConnection db = Open();
        using SqliteCommand command = db.CreateCommand();
        command.CommandText = """
            SELECT c.NamingCaseID, r.CaseRevisionID, r.RevisionNumber, c.Title, c.UpdatedAtUtc
            FROM NamingCase c JOIN CaseRevision r ON r.CaseRevisionID = (
                SELECT CaseRevisionID FROM CaseRevision WHERE forNamingCaseID=c.NamingCaseID
                ORDER BY RevisionNumber DESC, CaseRevisionID DESC LIMIT 1)
            WHERE c.State=0 ORDER BY c.UpdatedAtUtc DESC, c.NamingCaseID DESC
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        List<CaseSummary> cases = [];
        while (reader.Read()) cases.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? "未命名案件" : reader.GetString(3), reader.IsDBNull(4) ? "" : reader.GetString(4)));
        return cases;
    }

    public StoredCase Load(long namingCaseId)
    {
        using SqliteConnection db = Open();
        using SqliteCommand command = db.CreateCommand();
        command.CommandText = """
            SELECT r.CaseRevisionID, r.RevisionNumber, r.RawInputJson
            FROM NamingCase c JOIN CaseRevision r ON r.forNamingCaseID=c.NamingCaseID
            WHERE c.NamingCaseID=$id AND c.State=0
            ORDER BY r.RevisionNumber DESC, r.CaseRevisionID DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", namingCaseId);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("找不到可開啟的案件。");
        if (reader.IsDBNull(2)) throw new InvalidDataException("此案件版本缺少原始表單，無法安全開啟。");
        using JsonDocument snapshot = JsonDocument.Parse(reader.GetString(2));
        if (!snapshot.RootElement.TryGetProperty("formVersion", out JsonElement version) || version.GetInt32() != 1
            || !snapshot.RootElement.TryGetProperty("form", out JsonElement form))
            throw new InvalidDataException("此案件的表單格式尚不支援，請勿覆寫原始資料。");
        CaseInput input = form.Deserialize<CaseInput>(JsonOptions) ?? throw new InvalidDataException("案件表單內容無法讀取。");
        return new(namingCaseId, reader.GetInt64(0), reader.GetInt32(1), input);
    }

    public StoredCase Save(CaseInput input, long? namingCaseId = null, long? expectedRevisionId = null)
    {
        ValidatedCaseInput valid = CaseInputValidator.Validate(input);
        using SqliteConnection db = Open();
        using SqliteTransaction transaction = db.BeginTransaction(deferred: false);
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        string title = valid.Surname + "姓寶寶｜" + input.BirthDate;
        long caseId;
        int revisionNumber = 1;
        if (namingCaseId is null)
        {
            if (expectedRevisionId is not null) throw new CaseConflictException();
            using SqliteCommand create = db.CreateCommand();
            create.Transaction = transaction;
            create.CommandText = "INSERT INTO NamingCase (Title, CreatedAtUtc, UpdatedAtUtc) VALUES ($title,$now,$now); SELECT last_insert_rowid();";
            create.Parameters.AddWithValue("$title", title);
            create.Parameters.AddWithValue("$now", now);
            caseId = (long)create.ExecuteScalar()!;
        }
        else
        {
            caseId = namingCaseId.Value;
            using SqliteCommand current = db.CreateCommand();
            current.Transaction = transaction;
            current.CommandText = """
                SELECT c.State, r.CaseRevisionID, r.RevisionNumber FROM NamingCase c
                LEFT JOIN CaseRevision r ON r.CaseRevisionID=(SELECT CaseRevisionID FROM CaseRevision
                    WHERE forNamingCaseID=c.NamingCaseID ORDER BY RevisionNumber DESC, CaseRevisionID DESC LIMIT 1)
                WHERE c.NamingCaseID=$id
                """;
            current.Parameters.AddWithValue("$id", caseId);
            using SqliteDataReader reader = current.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0) || reader.GetInt32(0) != 0) throw new InvalidOperationException("找不到可更新的案件。");
            long? latest = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            if (latest != expectedRevisionId) throw new CaseConflictException();
            revisionNumber = reader.IsDBNull(2) ? 1 : checked(reader.GetInt32(2) + 1);
        }

        List<object> pending = [new { code = "location_resolution_pending", kind = "system", message = "出生地尚待系統解析與驗證。" }];
        using SqliteCommand insert = db.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO CaseRevision (forNamingCaseID,RevisionNumber,Surname,Gender,BirthDate,BirthTimeLocal,
                BirthCountry,BirthCity,BirthLocationDetail,CalendarDataJson,AvoidanceStatus,AvoidCharactersJson,AvoidMatchMode,
                RequiredCharacterStatus,RequiredCharactersJson,Remarks,RawInputJson,PendingIssuesJson,Status,CreatedAtUtc)
            VALUES ($case,$version,$surname,$gender,$date,$time,$country,$city,$detail,$calendar,$avoidStatus,$avoid,$mode,
                $requiredStatus,$required,$remarks,$raw,$pending,'needs_input',$now);
            SELECT last_insert_rowid();
            """;
        void Parameter(string name, object? value) => insert.Parameters.AddWithValue(name, value ?? DBNull.Value);
        Parameter("$case", caseId); Parameter("$version", revisionNumber); Parameter("$surname", valid.Surname);
        Parameter("$gender", input.Gender); Parameter("$date", input.BirthDate); Parameter("$time", valid.BirthTime);
        Parameter("$country", valid.Country); Parameter("$city", valid.City); Parameter("$detail", NullIfEmpty(valid.Detail));
        Parameter("$calendar", JsonSerializer.Serialize(valid.Lunar, JsonOptions));
        Parameter("$avoidStatus", input.NoAvoidanceCharacters ? "none" : valid.AvoidCharacters.Length == 0 ? "unprovided" : "provided");
        Parameter("$avoid", valid.AvoidCharacters.Length == 0 ? null : JsonSerializer.Serialize(valid.AvoidCharacters));
        Parameter("$mode", valid.AvoidCharacters.Length == 0 ? null : input.AvoidMatchMode);
        Parameter("$requiredStatus", input.NoRequiredCharacter ? "none" : valid.RequiredCharacter.Length == 0 ? "unprovided" : "provided");
        Parameter("$required", valid.RequiredCharacter.Length == 0 ? null : JsonSerializer.Serialize(new[] { new { character = valid.RequiredCharacter, position = input.RequiredPosition } }));
        Parameter("$remarks", NullIfEmpty(input.Remarks));
        Parameter("$raw", JsonSerializer.Serialize(new { formVersion = 1, form = input }, JsonOptions));
        Parameter("$pending", JsonSerializer.Serialize(pending)); Parameter("$now", now);
        long revisionId = (long)insert.ExecuteScalar()!;
        using SqliteCommand update = db.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE NamingCase SET Title=$title,UpdatedAtUtc=$now WHERE NamingCaseID=$id";
        update.Parameters.AddWithValue("$title", title); update.Parameters.AddWithValue("$now", now); update.Parameters.AddWithValue("$id", caseId);
        update.ExecuteNonQuery();
        transaction.Commit();
        return new(caseId, revisionId, revisionNumber, input);
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private SqliteConnection Open()
    {
        SqliteConnection db = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5
        }.ToString());
        try { db.Open(); return db; }
        catch { db.Dispose(); throw; }
    }
}
