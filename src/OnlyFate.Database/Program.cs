using System.Text.Encodings.Web;
using System.Text.Json;
using OnlyFate.Database;

if (args.Length is < 1 or > 2 || args[0] is not ("init" or "verify"))
{
    Console.Error.WriteLine("用法：OnlyFate.Database init|verify [database-path]");
    return 2;
}

string databasePath = args.Length == 2 ? args[1] : DatabaseSchema.DefaultDatabasePath;
try
{
    DatabaseVerification result = args[0] == "init" ? DatabaseSchema.Initialize(databasePath) : DatabaseSchema.Verify(databasePath);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }));
    return result.LogicalReferenceErrors.Count == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"資料庫操作失敗：{exception.Message}");
    return 1;
}
