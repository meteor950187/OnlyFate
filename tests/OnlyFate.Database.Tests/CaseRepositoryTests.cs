using Microsoft.Data.Sqlite;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public sealed class CaseRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OnlyFate.Database.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "cases.db");
    private static CaseInput ValidInput => new()
    {
        Surname = "林", Gender = "male", BirthDate = "2026-06-15", BirthHour = "12", BirthMinute = "00",
        BirthCountry = "台灣", BirthCity = "台北市", BirthLocationDetail = "虛構醫院",
        AvoidCharacters = "宏、志", AvoidMatchMode = "same_character",
        RequiredCharacter = "安", RequiredPosition = "last", Remarks = "希望有志氣，字要易讀。"
    };

    [Fact]
    public void NewCaseSurvivesReopeningRepositoryWithAllFormValues()
    {
        CaseRepository first = new(DatabasePath);
        StoredCase saved = first.Save(ValidInput);
        CaseRepository reopened = new(DatabasePath);
        StoredCase loaded = reopened.Load(saved.NamingCaseID);
        Assert.Equal(ValidInput, loaded.Input);
        Assert.Equal(1, loaded.RevisionNumber);
        Assert.Equal(saved.CaseRevisionID, loaded.CaseRevisionID);
        Assert.Equal(saved.NamingCaseID, Assert.Single(reopened.List()).NamingCaseID);
        using SqliteConnection db = Open();
        Assert.Equal("needs_input", Scalar(db, "SELECT Status FROM CaseRevision"));
        Assert.Equal(DBNull.Value, Scalar(db, "SELECT OrganizedContextJson FROM CaseRevision"));
        Assert.Equal(0L, Scalar(db, "SELECT count(*) FROM ExecutionCall"));
    }

    [Fact]
    public void SaveAddsRevisionWithoutChangingEarlierInputOrCreatingAnotherCase()
    {
        CaseRepository store = new(DatabasePath);
        StoredCase first = store.Save(ValidInput);
        CaseInput changed = ValidInput with { BirthMinute = "15", Remarks = "補充後的要求" };
        StoredCase second = store.Save(changed, first.NamingCaseID, first.CaseRevisionID);
        Assert.Equal(2, second.RevisionNumber);
        Assert.Equal(changed, store.Load(first.NamingCaseID).Input);
        Assert.Equal(second.CaseRevisionID, Assert.Single(store.List()).CaseRevisionID);
        using SqliteConnection db = Open();
        Assert.Equal(1L, Scalar(db, "SELECT count(*) FROM NamingCase"));
        Assert.Equal(2L, Scalar(db, "SELECT count(*) FROM CaseRevision"));
        Assert.Equal("12:00", Scalar(db, "SELECT BirthTimeLocal FROM CaseRevision WHERE RevisionNumber=1"));
        Assert.Equal("希望有志氣，字要易讀。", Scalar(db, "SELECT Remarks FROM CaseRevision WHERE RevisionNumber=1"));
    }

    [Fact]
    public void StaleSaveIsRejectedAndPreservesBothCurrentDataAndRevisionCount()
    {
        CaseRepository store = new(DatabasePath);
        StoredCase original = store.Save(ValidInput);
        CaseInput changed = ValidInput with { Remarks = "已更新" };
        store.Save(changed, original.NamingCaseID, original.CaseRevisionID);
        Assert.Throws<CaseConflictException>(() => store.Save(ValidInput, original.NamingCaseID, original.CaseRevisionID));
        Assert.Equal(changed, store.Load(original.NamingCaseID).Input);
        using SqliteConnection db = Open();
        Assert.Equal(2L, Scalar(db, "SELECT count(*) FROM CaseRevision"));
    }

    [Theory]
    [InlineData("surname")]
    [InlineData("gender")]
    [InlineData("date")]
    [InlineData("hour")]
    [InlineData("minute")]
    [InlineData("country")]
    [InlineData("city")]
    [InlineData("mode")]
    [InlineData("position")]
    public void MissingRequiredOrConditionalFieldDoesNotCreateAPartialCase(string field)
    {
        CaseRepository store = new(DatabasePath);
        CaseInput input = field switch
        {
            "surname" => ValidInput with { Surname = "" },
            "gender" => ValidInput with { Gender = "" },
            "date" => ValidInput with { BirthDate = "" },
            "hour" => ValidInput with { BirthHour = "" },
            "minute" => ValidInput with { BirthMinute = "" },
            "country" => ValidInput with { BirthCountry = "" },
            "city" => ValidInput with { BirthCity = "" },
            "mode" => ValidInput with { AvoidMatchMode = "" },
            _ => ValidInput with { RequiredPosition = "" }
        };
        Assert.Throws<CaseInputException>(() => store.Save(input));
        Assert.Empty(store.List());
        using SqliteConnection db = Open();
        Assert.Equal(0L, Scalar(db, "SELECT count(*) FROM CaseRevision"));
    }

    [Fact]
    public void ExplicitNoneAndUnprovidedRemainDistinctAfterReopening()
    {
        CaseRepository store = new(DatabasePath);
        CaseInput blank = ValidInput with { AvoidCharacters = "", AvoidMatchMode = "", RequiredCharacter = "", RequiredPosition = "" };
        StoredCase unprovided = store.Save(blank);
        StoredCase none = store.Save(blank with { NoAvoidanceCharacters = true, NoRequiredCharacter = true });
        Assert.False(store.Load(unprovided.NamingCaseID).Input.NoAvoidanceCharacters);
        Assert.True(store.Load(none.NamingCaseID).Input.NoAvoidanceCharacters);
        using SqliteConnection db = Open();
        Assert.Equal("unprovided", Scalar(db, "SELECT AvoidanceStatus FROM CaseRevision WHERE CaseRevisionID=1"));
        Assert.Equal("none", Scalar(db, "SELECT AvoidanceStatus FROM CaseRevision WHERE CaseRevisionID=2"));
    }

    [Fact]
    public void QuotesAndSqlLikeRemarksRoundTripAsText()
    {
        CaseRepository store = new(DatabasePath);
        CaseInput input = ValidInput with { Remarks = "家長說：''); DROP TABLE NamingCase; --\n希望平安。" };
        StoredCase saved = store.Save(input);
        Assert.Equal(input.Remarks, new CaseRepository(DatabasePath).Load(saved.NamingCaseID).Input.Remarks);
        Assert.Single(store.List());
    }

    [Fact]
    public void DisabledOrMissingParentCannotReceiveNewRevision()
    {
        CaseRepository store = new(DatabasePath);
        StoredCase saved = store.Save(ValidInput);
        using (SqliteConnection db = Open())
        {
            using SqliteCommand command = db.CreateCommand();
            command.CommandText = "UPDATE NamingCase SET State=1";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => store.Save(ValidInput, saved.NamingCaseID, saved.CaseRevisionID));
        Assert.Throws<InvalidOperationException>(() => store.Save(ValidInput, 999, saved.CaseRevisionID));
        Assert.Empty(store.List());
        using SqliteConnection check = Open();
        Assert.Equal(1L, Scalar(check, "SELECT count(*) FROM CaseRevision"));
    }

    [Theory]
    [InlineData("2026-06-15", 2026, 5, 1, false)]
    [InlineData("2025-07-24", 2025, 6, 30, false)]
    [InlineData("2025-07-25", 2025, 6, 1, true)]
    [InlineData("2025-08-23", 2025, 7, 1, false)]
    public void CivilDateConversionMatchesPreviouslyCrossCheckedFixtures(string solar, int year, int month, int day, bool leap)
    {
        CivilLunarDate lunar = CivilLunarCalendar.Convert(DateOnly.Parse(solar));
        Assert.Equal((year, month, day, leap), (lunar.Year, lunar.Month, lunar.Day, lunar.IsLeapMonth));
        Assert.Equal("original_civil_date", lunar.Basis);
    }

    [Fact]
    public void CivilBirthdayRemainsUnchangedNearMidnight()
    {
        CaseRepository store = new(DatabasePath);
        store.Save(ValidInput with { BirthHour = "23", BirthMinute = "58" });
        using SqliteConnection db = Open();
        Assert.Equal("2026-06-15", Scalar(db, "SELECT BirthDate FROM CaseRevision"));
        Assert.Equal(1L, Scalar(db, "SELECT json_extract(CalendarDataJson,'$.day') FROM CaseRevision"));
        Assert.Equal(DBNull.Value, Scalar(db, "SELECT BirthTimeZoneID FROM CaseRevision"));
    }

    [Theory]
    [InlineData("24", "00")]
    [InlineData("12", "60")]
    [InlineData("1", "00")]
    public void InvalidTimeDoesNotProduceAFallbackBirthTime(string hour, string minute)
    {
        CaseRepository store = new(DatabasePath);
        Assert.Throws<CaseInputException>(() => store.Save(ValidInput with { BirthHour = hour, BirthMinute = minute }));
        Assert.Empty(store.List());
    }

    [Fact]
    public void RevisionInsertFailureRollsBackTheParentCaseWrite()
    {
        CaseRepository store = new(DatabasePath);
        using (SqliteConnection db = Open())
        {
            using SqliteCommand reject = db.CreateCommand();
            reject.CommandText = "CREATE TRIGGER RejectTestRevision BEFORE INSERT ON CaseRevision BEGIN SELECT RAISE(ABORT,'test-only failure'); END";
            reject.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => store.Save(ValidInput));
        Assert.Empty(store.List());
        using SqliteConnection check = Open();
        Assert.Equal(0L, Scalar(check, "SELECT count(*) FROM NamingCase"));
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("1800-01-01")]
    public void InvalidOrUnsupportedDateIsNotGuessedOrSaved(string date)
    {
        CaseRepository store = new(DatabasePath);
        Assert.Throws<CaseInputException>(() => store.Save(ValidInput with { BirthDate = date }));
        Assert.Empty(store.List());
    }

    private SqliteConnection Open()
    {
        SqliteConnection db = new(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private static object Scalar(SqliteConnection db, string sql)
    {
        using SqliteCommand command = db.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OnlyFate.Database.Tests")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
