using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace OnlyFate.Database;

public static class BirthTimeResolver
{
    public static BirthTimeReceipt Resolve(CaseInput input, string timeZoneId)
    {
        ValidatedCaseInput valid = CaseInputValidator.Validate(input);
        DateTimeZone? zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId);
        if (zone is null) throw new Step2Exception("TIMEZONE_NOT_FOUND", "找不到出生地的時區規則，不能以目前時差代替。");
        LocalDateTime local = new(valid.BirthDate.Year, valid.BirthDate.Month, valid.BirthDate.Day,
            int.Parse(input.BirthHour, CultureInfo.InvariantCulture), int.Parse(input.BirthMinute, CultureInfo.InvariantCulture), 0);
        var map = zone.MapLocal(local);
        if (map.Count == 0) throw new Step2Exception("NONEXISTENT_CIVIL_TIME", "這個出生時刻落在夏令時間跳時的空檔，請重新確認出生紀錄。");
        if (map.Count == 2) throw new Step2Exception("AMBIGUOUS_CIVIL_TIME", "這個出生時刻因調整時鐘出現兩次，需補充當時的時間依據，不能自行選擇。");
        ZonedDateTime zoned = map.Single();
        var interval = zone.GetZoneInterval(zoned.ToInstant());
        return new(local.ToString("uuuu-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), timeZoneId,
            DateTimeZoneProviders.Tzdb.VersionId, (interval.WallOffset.Seconds - interval.Savings.Seconds) / 3600d,
            interval.Savings.Seconds / 3600d, zoned.Offset.Seconds / 3600d, InstantPattern.ExtendedIso.Format(zoned.ToInstant()));
    }

    public static IReadOnlyDictionary<string, object?> McpArguments(CaseInput input, BirthplaceCandidate place, BirthTimeReceipt time)
    {
        DateOnly date = DateOnly.ParseExact(input.BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (date.Year is < 1800 or > 2100) throw new Step2Exception("ENGINE_DATE_RANGE", "目前 OpenFate MCP 支援 1800 至 2100 年出生資料。");
        return new Dictionary<string, object?>
        {
            ["year"] = date.Year, ["month"] = date.Month, ["day"] = date.Day,
            ["hour"] = int.Parse(input.BirthHour, CultureInfo.InvariantCulture), ["minute"] = int.Parse(input.BirthMinute, CultureInfo.InvariantCulture),
            ["second"] = 0, ["gender"] = input.Gender, ["longitude"] = place.Longitude,
            ["timezone"] = time.StandardOffsetHours, ["timezoneId"] = time.TimeZoneID, ["dstOffset"] = time.DstOffsetHours,
            ["calendarType"] = "solar", ["enableTrueSolarTime"] = true, ["dayBoundaryMode"] = "ZI_HOUR_23"
        };
    }
}
