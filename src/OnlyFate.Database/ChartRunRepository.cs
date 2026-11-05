using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OnlyFate.Database;

internal sealed class ChartRunRepository(string databasePath)
{
    internal (long Run, long Step) Start(long caseId, long revisionId, JsonElement input, int stepNumber = 2)
    {
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction(deferred: false);
        AssertCurrent(db, tx, caseId, revisionId);
        long run = (long)Scalar(db, tx, "INSERT INTO ProcessingRun (forCaseRevisionID,PolicyJson,Status,StartedAtUtc) VALUES ($revision,$policy,'running',$now); SELECT last_insert_rowid();",
            ("$revision", revisionId), ("$policy", input.GetProperty("policy").GetRawText()), ("$now", Now()))!;
        long step = (long)Scalar(db, tx, "INSERT INTO ProcessingStep (forProcessingRunID,StepNumber,InputJson,Status,StartedAtUtc) VALUES ($run,$number,$input,'running',$now); SELECT last_insert_rowid();",
            ("$run", run), ("$number", stepNumber), ("$input", input.GetRawText()), ("$now", Now()))!;
        if (input.GetProperty("policy").TryGetProperty("ruleVersion", out var ruleVersion)) Execute(db, tx, "UPDATE ProcessingRun SET RuleSetVersion=$version WHERE ProcessingRunID=$run", ("$version", ruleVersion.GetString()), ("$run", run));
        tx.Commit(); return (run, step);
    }

    internal void SetInput(long step, JsonElement input)
    {
        using SqliteConnection db = Open();
        Execute(db, null, "UPDATE ProcessingStep SET InputJson=$input WHERE ProcessingStepID=$step", ("$input", input.GetRawText()), ("$step", step));
    }

    internal long StartCall(long step, int number, string name, IReadOnlyDictionary<string, object?> arguments, string version)
    {
        using SqliteConnection db = Open();
        return (long)Scalar(db, null, """
            INSERT INTO ExecutionCall (forProcessingStepID,CallNumber,CallKind,Provider,Operation,ToolName,ToolVersion,RequestJson)
            VALUES ($step,$number,'tool','OpenFate',$name,$name,$version,$request); SELECT last_insert_rowid();
            """, ("$step", step), ("$number", number), ("$name", name), ("$version", version),
            ("$request", Step2Json.Element(new { name, arguments }).GetRawText()))!;
    }

    internal void FinishCall(long call, string status, JsonElement? response, long milliseconds, string? errorCode = null)
    {
        using SqliteConnection db = Open();
        Execute(db, null, "UPDATE ExecutionCall SET Status=$status,ResponseJson=$response,DurationMilliseconds=$duration,ErrorCode=$code,FinishedAtUtc=$now WHERE ExecutionCallID=$id",
            ("$status", status), ("$response", response?.GetRawText()), ("$duration", milliseconds), ("$code", errorCode), ("$now", Now()), ("$id", call));
    }

    internal void End(long run, long step, string status, JsonElement? output, string? errorCode, string? message, long caseId = 0, long revisionId = 0, long? expectedChartRunId = null, long? expectedAnalysisRunId = null, long? expectedNamingRunId = null, Func<SqliteConnection, SqliteTransaction, JsonElement>? completedWrite = null)
    {
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction(deferred: false);
        if (status == "completed") AssertCurrent(db, tx, caseId, revisionId);
        if (status == "completed" && expectedChartRunId is long expected)
        {
            object? source = Scalar(db, tx, """
                SELECT CASE WHEN s.Status='completed' THEN r.ProcessingRunID ELSE NULL END
                FROM ProcessingRun r JOIN ProcessingStep s ON s.forProcessingRunID=r.ProcessingRunID
                WHERE r.forCaseRevisionID=$revision AND s.StepNumber=2 ORDER BY s.ProcessingStepID DESC LIMIT 1
                """, ("$revision", revisionId));
            if (source is not long id || id != expected) throw new CaseConflictException();
        }
        if (status == "completed" && expectedAnalysisRunId is long analysis)
        {
            object? source = Scalar(db, tx, """
                SELECT CASE WHEN s.Status='completed' THEN r.ProcessingRunID ELSE NULL END
                FROM ProcessingRun r JOIN ProcessingStep s ON s.forProcessingRunID=r.ProcessingRunID
                WHERE r.forCaseRevisionID=$revision AND s.StepNumber=3 ORDER BY s.ProcessingStepID DESC LIMIT 1
                """, ("$revision", revisionId));
            if (source is not long id || id != analysis) throw new CaseConflictException();
        }
        if (status == "completed" && expectedNamingRunId is long naming)
            ReportSql.AssertStep(db, tx, revisionId, 4, naming);
        if (status == "completed" && completedWrite is not null) output = completedWrite(db, tx);
        Execute(db, tx, "UPDATE ProcessingStep SET Status=$status,OutputJson=$output,ValidationJson=$validation,FinishedAtUtc=$now WHERE ProcessingStepID=$step",
            ("$status", status), ("$output", output?.GetRawText()), ("$validation", Step2Json.Element(new { passed = status == "completed", errorCode, message }).GetRawText()), ("$now", Now()), ("$step", step));
        Execute(db, tx, "UPDATE ProcessingRun SET Status=$status,ErrorCode=$code,ErrorMessage=$message,FinishedAtUtc=$finished WHERE ProcessingRunID=$run",
            ("$status", status == "completed" ? "waiting" : status), ("$code", errorCode), ("$message", message),
            ("$finished", status == "completed" ? null : Now()), ("$run", run));
        tx.Commit();
    }

    internal ChartRunResult? Latest(long revisionId, int stepNumber = 2)
    {
        using SqliteConnection db = Open(); using SqliteCommand command = db.CreateCommand();
        command.CommandText = """
            SELECT r.ProcessingRunID,s.Status,s.InputJson,s.OutputJson,r.ErrorMessage, CASE WHEN r.forCaseRevisionID=(SELECT cr.CaseRevisionID FROM CaseRevision cr JOIN NamingCase nc ON nc.NamingCaseID=cr.forNamingCaseID WHERE cr.forNamingCaseID=(SELECT forNamingCaseID FROM CaseRevision WHERE CaseRevisionID=$revision) AND nc.State=0 ORDER BY cr.RevisionNumber DESC,cr.CaseRevisionID DESC LIMIT 1) THEN 1 ELSE 0 END FROM ProcessingRun r
            JOIN ProcessingStep s ON s.forProcessingRunID=r.ProcessingRunID
            WHERE r.forCaseRevisionID=$revision AND s.StepNumber=$number ORDER BY s.ProcessingStepID DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        command.Parameters.AddWithValue("$number", stepNumber);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        using JsonDocument inputDoc = JsonDocument.Parse(reader.GetString(2));
        JsonElement? chart = null;
        if (!reader.IsDBNull(3)) { using JsonDocument output = JsonDocument.Parse(reader.GetString(3)); chart = output.RootElement.Clone(); }
        if (reader.GetInt64(5) != 1) return new(reader.GetInt64(0), revisionId, "cancelled", inputDoc.RootElement.Clone(), null, "案件已變版，請重新執行。");
        return new(reader.GetInt64(0), revisionId, reader.GetString(1), inputDoc.RootElement.Clone(), chart, reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    internal long StartModelCall(long step, int number, string provider, string operation, string model, JsonElement request, string promptVersion)
    {
        using var db = Open();
        return (long)Scalar(db, null, """
            INSERT INTO ExecutionCall(forProcessingStepID,CallNumber,CallKind,Provider,Operation,ModelName,PromptVersion,RequestJson)
            VALUES($step,$number,'model',$provider,$operation,$model,$version,$request); SELECT last_insert_rowid();
            """, ("$step", step), ("$number", number), ("$provider", provider), ("$operation", operation), ("$model", model), ("$version", promptVersion), ("$request", request.GetRawText()))!;
    }

    internal void ModelUsage(long call, ModelResult result)
    {
        using var db = Open();
        Execute(db, null, "UPDATE ExecutionCall SET ProviderRequestID=$id,ModelName=coalesce($model,ModelName),InputTokens=$input,OutputTokens=$output WHERE ExecutionCallID=$call",
            ("$id", result.ProviderRequestId), ("$model", result.ActualModel), ("$input", result.InputTokens), ("$output", result.OutputTokens), ("$call", call));
    }

    private static void AssertCurrent(SqliteConnection db, SqliteTransaction tx, long caseId, long revisionId)
    {
        object? latest = Scalar(db, tx, """
            SELECT r.CaseRevisionID FROM NamingCase c JOIN CaseRevision r ON r.forNamingCaseID=c.NamingCaseID
            WHERE c.NamingCaseID=$case AND c.State=0 ORDER BY r.RevisionNumber DESC,r.CaseRevisionID DESC LIMIT 1
            """, ("$case", caseId));
        if (latest is not long id || id != revisionId) throw new CaseConflictException();
    }

    private SqliteConnection Open()
    {
        SqliteConnection db = new(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString());
        try { db.Open(); return db; } catch { db.Dispose(); throw; }
    }

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, (string, object?)[] parameters)
    {
        SqliteCommand command = db.CreateCommand(); command.Transaction = tx; command.CommandText = sql;
        foreach ((string key, object? value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }
    private static object? Scalar(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] parameters)
    { using SqliteCommand command = Command(db, tx, sql, parameters); return command.ExecuteScalar(); }
    private static void Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] parameters)
    { using SqliteCommand command = Command(db, tx, sql, parameters); command.ExecuteNonQuery(); }
}
