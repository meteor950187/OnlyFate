using System.Text.Json;

namespace OnlyFate.Database;

public sealed record BirthplaceCandidate(long GeoNameID, string Name, string CountryCode, string Region,
    double Latitude, double Longitude, string TimeZoneID, string SourceVersion)
{
    public string Display => $"{Name}｜{CountryCode} · {Region}（城市代表點）";
}

public sealed record BirthTimeReceipt(string LocalCivilTime, string TimeZoneID, string TzdbVersion,
    double StandardOffsetHours, double DstOffsetHours, double UtcOffsetHours, string BirthUtc,
    string InputPrecision = "minute", int CalculationSecond = 0);

public sealed class Step2Exception(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public interface IOpenFateClient : IAsyncDisposable
{
    string ServerVersion { get; }
    Task<JsonElement> CallAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken);
}

public sealed record ChartRunResult(long ProcessingRunID, long CaseRevisionID, string Status,
    JsonElement Input, JsonElement? Chart, string? ErrorMessage);

public static class Step2Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}
