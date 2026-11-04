using Microsoft.Data.Sqlite;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public sealed class DatabaseSchemaTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OnlyFate.Database.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "test.db");

    [Fact]
    public void InitializeCreatesNineStrictTablesWithoutPhysicalForeignKeys()
    {
        DatabaseVerification result = DatabaseSchema.Initialize(DatabasePath);
        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal(9, result.Tables.Count);
        Assert.Empty(result.LogicalReferenceErrors);
        using SqliteConnection db = Open();
        Assert.Equal(9L, Scalar(db, "SELECT count(*) FROM pragma_table_list WHERE schema='main' AND strict=1"));
        foreach (string table in result.Tables)
        {
            Assert.Equal(0L, Scalar(db, $"SELECT count(*) FROM pragma_foreign_key_list('{table}')"));
            Assert.Equal(0L, Scalar(db, $"SELECT count(*) FROM pragma_table_info('{table}') WHERE pk=0 AND [notnull]=1"));
        }
    }

    [Fact]
    public void InitializeAgainPreservesExistingDataAndOneMigrationRecord()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using (SqliteConnection db = Open())
        {
            Execute(db, "INSERT INTO NamingCase (Title) VALUES ('虛構測試案件')");
        }
        DatabaseSchema.Initialize(DatabasePath);
        using SqliteConnection check = Open();
        Assert.Equal("虛構測試案件", Scalar(check, "SELECT Title FROM NamingCase"));
        Assert.Equal(1L, Scalar(check, "SELECT count(*) FROM SchemaMigration"));
    }

    [Fact]
    public void DraftKeepsUnknownBirthFactsNullAndCannotBecomeReady()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using SqliteConnection db = Open();
        Execute(db, "INSERT INTO CaseRevision DEFAULT VALUES");
        Assert.Equal(DBNull.Value, Scalar(db, "SELECT BirthTimeLocal FROM CaseRevision"));
        Assert.Equal("unprovided", Scalar(db, "SELECT AvoidanceStatus FROM CaseRevision"));
        AssertConstraint(db, "UPDATE CaseRevision SET Status='ready'");
    }

    [Theory]
    [InlineData("BirthDate", "2025-02-29")]
    [InlineData("BirthDate", "2026-13-01")]
    [InlineData("BirthDate", "2026-6-15")]
    [InlineData("BirthTimeLocal", "24:00")]
    [InlineData("BirthTimeLocal", "12:60")]
    [InlineData("BirthTimeLocal", "12:00:00")]
    public void InvalidCivilDateOrMinuteTimeIsRejected(string column, string value)
    {
        DatabaseSchema.Initialize(DatabasePath);
        using SqliteConnection db = Open();
        using SqliteCommand command = db.CreateCommand();
        command.CommandText = $"INSERT INTO CaseRevision ({column}) VALUES ($value)";
        command.Parameters.AddWithValue("$value", value);
        Assert.Equal(19, Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).SqliteErrorCode);
    }

    [Fact]
    public void GregorianAndLunarFactsAreStoredSeparatelyWithoutInventingSeconds()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using SqliteConnection db = Open();
        Execute(db, """
            INSERT INTO CaseRevision (Surname, BirthDate, BirthTimeLocal, CalendarDataJson)
            VALUES ('林', '2026-06-15', '23:58', '{"month":5,"day":1,"isLeapMonth":false,"basis":"civil"}')
            """);
        Assert.Equal("23:58", Scalar(db, "SELECT BirthTimeLocal FROM CaseRevision"));
        Assert.Equal(1L, Scalar(db, "SELECT json_extract(CalendarDataJson,'$.day') FROM CaseRevision"));
        AssertConstraint(db, "INSERT INTO CaseRevision (RawInputJson) VALUES ('invalid-json')");
        AssertConstraint(db, "INSERT INTO CaseRevision (AvoidCharactersJson) VALUES ('{}')");
    }

    [Fact]
    public void PartialUniqueIndexRejectsDuplicateRevisionButAllowsIncompleteReferences()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using SqliteConnection db = Open();
        Execute(db, "INSERT INTO NamingCase DEFAULT VALUES; INSERT INTO CaseRevision (forNamingCaseID) VALUES (1)");
        AssertConstraint(db, "INSERT INTO CaseRevision (forNamingCaseID) VALUES (1)");
        Execute(db, "INSERT INTO CaseRevision DEFAULT VALUES; INSERT INTO CaseRevision DEFAULT VALUES");
        Assert.Equal(3L, Scalar(db, "SELECT count(*) FROM CaseRevision"));
        AssertConstraint(db, "INSERT INTO NamingCase (State) VALUES (2)");
    }

    [Fact]
    public void VerificationFindsLogicalOrphansInsteadOfPretendingForeignKeysExist()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using (SqliteConnection db = Open())
        {
            Execute(db, "INSERT INTO ProcessingRun (forCaseRevisionID) VALUES (999)");
        }
        Assert.Single(DatabaseSchema.Verify(DatabasePath).LogicalReferenceErrors);
    }

    [Fact]
    public void FailedTransactionLeavesNoPartialCaseWrite()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using SqliteConnection db = Open();
        using (SqliteTransaction transaction = db.BeginTransaction())
        {
            using SqliteCommand insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO NamingCase (Title) VALUES ('應回滾'); INSERT INTO NamingCase (State) VALUES (9)";
            Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());
            transaction.Rollback();
        }
        Assert.Equal(0L, Scalar(db, "SELECT count(*) FROM NamingCase"));
    }

    [Fact]
    public void InitializeRefusesAnUnrelatedExistingDatabase()
    {
        Directory.CreateDirectory(directory);
        using (SqliteConnection db = Open())
        {
            Execute(db, "CREATE TABLE OtherData (ID INTEGER PRIMARY KEY); INSERT INTO OtherData VALUES (1)");
        }
        Assert.Throws<InvalidOperationException>(() => DatabaseSchema.Initialize(DatabasePath));
        using SqliteConnection check = Open();
        Assert.Equal(1L, Scalar(check, "SELECT count(*) FROM OtherData"));
    }

    [Fact]
    public void InitializeRefusesFutureVersionOrChangedMigrationChecksum()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using (SqliteConnection db = Open())
        {
            Execute(db, "PRAGMA user_version=2");
        }
        Assert.Throws<InvalidOperationException>(() => DatabaseSchema.Initialize(DatabasePath));
        using (SqliteConnection db = Open())
        {
            Execute(db, "PRAGMA user_version=1; UPDATE SchemaMigration SET ScriptSha256='" + new string('0', 64) + "'");
        }
        Assert.Throws<InvalidOperationException>(() => DatabaseSchema.Initialize(DatabasePath));
    }

    [Fact]
    public void CompleteInputAndBothCallKindsCanTraceToOneReportWithTwoFormats()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using (SqliteConnection db = Open())
        {
            Execute(db, """
                INSERT INTO NamingCase (Title) VALUES ('虛構關聯測試');
                INSERT INTO CaseRevision (forNamingCaseID, Surname, Gender, BirthDate, BirthTimeLocal,
                    BirthCountry, BirthCity, CalendarDataJson, OrganizedContextJson, PendingIssuesJson, ValidatedAtUtc, Status)
                VALUES (1, '林', 'male', '2026-06-15', '12:00', '台灣', '台北市',
                    '{"month":5,"day":1,"isLeapMonth":false,"basis":"civil"}', '{}', '[]', '2026-11-02T00:00:00.000Z', 'ready');
                INSERT INTO ProcessingRun (forCaseRevisionID, RuleSetVersion) VALUES (1, 'test-only-unverified');
                INSERT INTO ProcessingStep (forProcessingRunID, StepNumber) VALUES (1, 2);
                INSERT INTO ProcessingStep (forProcessingRunID, StepNumber) VALUES (1, 3);
                INSERT INTO ExecutionCall (forProcessingStepID, CallKind, ToolName, Provider)
                VALUES (1, 'tool', 'test-only-tool', 'synthetic');
                INSERT INTO ExecutionCall (forProcessingStepID, CallKind, ModelName, Provider)
                VALUES (2, 'model', 'test-only-model', 'synthetic');
                INSERT INTO NamingReport (forProcessingRunID, ReportDataJson) VALUES (1, '{"testOnly":true}');
                INSERT INTO ReportArtifact (forNamingReportID, Format) VALUES (1, 'html');
                INSERT INTO ReportArtifact (forNamingReportID, Format) VALUES (1, 'pdf');
                """);
            Assert.Equal(2L, Scalar(db, "SELECT count(*) FROM ExecutionCall WHERE CallKind IN ('model','tool')"));
            Assert.Equal(2L, Scalar(db, "SELECT count(*) FROM ReportArtifact WHERE forNamingReportID=1"));
            AssertConstraint(db, "INSERT INTO ReportArtifact (forNamingReportID, Format) VALUES (1, 'pdf')");
            AssertConstraint(db, "UPDATE NamingReport SET Status='ready'");
            AssertConstraint(db, "UPDATE ReportArtifact SET Status='ready'");
        }
        Assert.Empty(DatabaseSchema.Verify(DatabasePath).LogicalReferenceErrors);
    }

    [Fact]
    public void ANewRevisionAndRetryPreserveEarlierInputAndStepOutput()
    {
        DatabaseSchema.Initialize(DatabasePath);
        using SqliteConnection db = Open();
        Execute(db, """
            INSERT INTO NamingCase DEFAULT VALUES;
            INSERT INTO CaseRevision (forNamingCaseID, RevisionNumber, Remarks) VALUES (1, 1, '原始需求');
            INSERT INTO CaseRevision (forNamingCaseID, RevisionNumber, Remarks) VALUES (1, 2, '已補充需求');
            INSERT INTO ProcessingRun (forCaseRevisionID) VALUES (1);
            INSERT INTO ProcessingStep (forProcessingRunID, StepNumber, AttemptNumber, OutputJson, Status)
            VALUES (1, 4, 1, '{"testOnly":true,"rejected":true}', 'failed');
            INSERT INTO ProcessingStep (forProcessingRunID, StepNumber, AttemptNumber, OutputJson)
            VALUES (1, 4, 2, '{"testOnly":true,"retry":true}');
            """);
        Assert.Equal("原始需求", Scalar(db, "SELECT Remarks FROM CaseRevision WHERE CaseRevisionID=1"));
        Assert.Equal(2L, Scalar(db, "SELECT count(*) FROM ProcessingStep WHERE forProcessingRunID=1 AND StepNumber=4"));
        AssertConstraint(db, "INSERT INTO ProcessingStep (forProcessingRunID, StepNumber, AttemptNumber) VALUES (1, 4, 2)");
    }

    [Fact]
    public void VerifyDoesNotCreateAMissingDatabase()
    {
        Assert.Throws<FileNotFoundException>(() => DatabaseSchema.Verify(DatabasePath));
        Assert.False(File.Exists(DatabasePath));
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

    private static void Execute(SqliteConnection db, string sql)
    {
        using SqliteCommand command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void AssertConstraint(SqliteConnection db, string sql) =>
        Assert.Equal(19, Assert.Throws<SqliteException>(() => Execute(db, sql)).SqliteErrorCode);

    public void Dispose()
    {
        string testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OnlyFate.Database.Tests")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(directory);
        if (!target.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing cleanup outside the test workspace.");
        }
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
