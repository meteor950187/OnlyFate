using System.Text.Json;
using NodaTime.Text;

namespace OnlyFate.Database;

internal static class ChartValidator
{
    internal static JsonElement Payload(JsonElement response)
    {
        if (response.TryGetProperty("isError", out JsonElement error) && error.ValueKind == JsonValueKind.True)
            throw new Step2Exception("MCP_TOOL_ERROR", "OpenFate 回報工具錯誤，原始回傳已保留。");
        if (response.TryGetProperty("structuredContent", out JsonElement structured) && structured.ValueKind == JsonValueKind.Object) return structured.Clone();
        foreach (JsonElement content in response.GetProperty("content").EnumerateArray())
            if (content.GetProperty("type").GetString() == "text")
            {
                using JsonDocument parsed = JsonDocument.Parse(content.GetProperty("text").GetString()!);
                return parsed.RootElement.Clone();
            }
        throw new Step2Exception("MCP_RESULT_INCOMPLETE", "OpenFate 沒有回傳可讀取的排盤資料。");
    }

    internal static JsonElement Validate(JsonElement response, IReadOnlyDictionary<string, object?> arguments, BirthTimeReceipt time)
    {
        try
        {
            JsonElement chart = Payload(response).GetProperty("data").GetProperty("chart");
            foreach (string name in new[] { "year", "month", "day", "hour" })
            {
                JsonElement pillar = chart.GetProperty("pillars").GetProperty(name);
                if (pillar.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(pillar.GetProperty("ganZhi").GetString())
                    || pillar.GetProperty("hiddenStems").ValueKind != JsonValueKind.Array || pillar.GetProperty("hiddenStems").GetArrayLength() == 0)
                    throw new Step2Exception("MCP_RESULT_INCOMPLETE", "排盤缺少四柱或藏干，不能視為完整結果。");
                string? stem = pillar.GetProperty("stem").GetString(), branch = pillar.GetProperty("branch").GetString();
                if (stem is null || stem.Length != 1 || !"甲乙丙丁戊己庚辛壬癸".Contains(stem, StringComparison.Ordinal)
                    || branch is null || branch.Length != 1 || !"子丑寅卯辰巳午未申酉戌亥".Contains(branch, StringComparison.Ordinal)
                    || pillar.GetProperty("ganZhi").GetString() != stem + branch)
                    throw new Step2Exception("MCP_RESULT_INCOMPLETE", "四柱干支格式不一致，已停止採用結果。");
                foreach (JsonElement hidden in pillar.GetProperty("hiddenStems").EnumerateArray())
                {
                    string? hiddenStem = hidden.GetProperty("stem").GetString();
                    if (hiddenStem is null || hiddenStem.Length != 1 || !"甲乙丙丁戊己庚辛壬癸".Contains(hiddenStem, StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(hidden.GetProperty("element").GetString()))
                        throw new Step2Exception("MCP_RESULT_INCOMPLETE", "藏干資料格式不完整。");
                }
            }
            if (chart.GetProperty("dayMaster").GetProperty("char").GetString() != chart.GetProperty("pillars").GetProperty("day").GetProperty("stem").GetString())
                throw new Step2Exception("MCP_RESULT_INCOMPLETE", "日主與日柱天干不一致。");
            JsonElement civil = chart.GetProperty("calendar").GetProperty("civilSolar");
            foreach (string name in new[] { "year", "month", "day", "hour", "minute", "second" })
                if (civil.GetProperty(name).GetInt32() != Convert.ToInt32(arguments[name]))
                    throw new Step2Exception("MCP_INPUT_MISMATCH", "工具回傳的出生資料與送入資料不一致，已停止採用結果。");
            JsonElement solar = chart.GetProperty("calendar").GetProperty("calculationSolar");
            _ = new DateTime(solar.GetProperty("year").GetInt32(), solar.GetProperty("month").GetInt32(), solar.GetProperty("day").GetInt32(),
                solar.GetProperty("hour").GetInt32(), solar.GetProperty("minute").GetInt32(), solar.GetProperty("second").GetInt32());
            JsonElement metadata = chart.GetProperty("metadata");
            if (!metadata.GetProperty("trueSolarTimeApplied").GetBoolean()
                || metadata.GetProperty("dayBoundaryMode").GetString() != "ZI_HOUR_23"
                || Math.Abs(metadata.GetProperty("timezoneBasis").GetDouble() - time.StandardOffsetHours) > 0.0000001
                || Math.Abs(metadata.GetProperty("dstOffset").GetDouble() - time.DstOffsetHours) > 0.0000001
                || Math.Abs(metadata.GetProperty("longitude").GetDouble() - Convert.ToDouble(arguments["longitude"])) > 0.0000001)
                throw new Step2Exception("MCP_POLICY_MISMATCH", "工具實際採用的排盤政策與本次設定不同。");
            JsonElement timing = chart.GetProperty("daYun").GetProperty("timing");
            if (timing.GetProperty("status").GetString() != "CALCULATED" || timing.GetProperty("version").GetString() != "DAYUN_SECOND_V2")
                throw new Step2Exception("MCP_TIMING_UNAVAILABLE", "工具未能完成時間解析，不能以舊式 fallback 數字當成完整結果。");
            if (Math.Abs(timing.GetProperty("resolvedOffsetSeconds").GetDouble() - time.UtcOffsetHours * 3600) > 0.001
                || InstantPattern.ExtendedIso.Parse(timing.GetProperty("birthUtc").GetString()!).Value != InstantPattern.ExtendedIso.Parse(time.BirthUtc).Value)
                throw new Step2Exception("TIMEZONE_RULE_MISMATCH", "本機與排盤工具的出生時區解析不一致，請先核對時間規則。");
            return chart.Clone();
        }
        catch (Exception exception) when (exception is not Step2Exception && exception is (JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentOutOfRangeException))
        {
            throw new Step2Exception("MCP_RESULT_INCOMPLETE", "排盤回傳格式或必要資料不完整，原始回傳已保留。");
        }
    }
}
