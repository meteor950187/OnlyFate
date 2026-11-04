using System.Globalization;

namespace OnlyFate.Database;

public sealed record CivilLunarDate(int Year, int Month, int Day, bool IsLeapMonth, string Basis, string Source)
{
    public string Display => $"農曆：{Year} 年{(IsLeapMonth ? "閏" : "")}{Month}月{Day}日";
}

public static class CivilLunarCalendar
{
    public static CivilLunarDate Convert(DateOnly birthDate)
    {
        ChineseLunisolarCalendar calendar = new();
        DateTime civilDate = birthDate.ToDateTime(TimeOnly.MinValue);
        if (civilDate < calendar.MinSupportedDateTime || civilDate > calendar.MaxSupportedDateTime)
        {
            throw new ArgumentOutOfRangeException(nameof(birthDate),
                $"目前農曆換算支援 {calendar.MinSupportedDateTime:yyyy-MM-dd} 至 {calendar.MaxSupportedDateTime:yyyy-MM-dd}，不會自行推估範圍外日期。");
        }
        int year = calendar.GetYear(civilDate);
        int ordinalMonth = calendar.GetMonth(civilDate);
        int leapMonth = calendar.GetLeapMonth(year);
        bool isLeapMonth = leapMonth != 0 && ordinalMonth == leapMonth;
        int month = ordinalMonth - (leapMonth != 0 && ordinalMonth >= leapMonth ? 1 : 0);
        return new(year, month, calendar.GetDayOfMonth(civilDate), isLeapMonth,
            "original_civil_date", "System.Globalization.ChineseLunisolarCalendar/.NET " + Environment.Version);
    }
}
