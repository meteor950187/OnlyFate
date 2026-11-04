using System.Globalization;
using System.Text;

namespace OnlyFate.Database;

internal sealed record ValidatedCaseInput(string Surname, DateOnly BirthDate, string BirthTime,
    string Country, string City, string Detail, string[] AvoidCharacters, string RequiredCharacter, CivilLunarDate Lunar);

internal static class CaseInputValidator
{
    internal static ValidatedCaseInput Validate(CaseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<string> errors = [];
        string surname = input.Surname?.Trim() ?? "";
        if (surname.EnumerateRunes().Count() != 1 || !surname.EnumerateRunes().All(Rune.IsLetter))
            errors.Add("請填寫單字姓氏。");
        if (input.Gender is not ("male" or "female")) errors.Add("請選擇寶寶性別。");
        CivilLunarDate? lunar = null;
        bool validDate = DateOnly.TryParseExact(input.BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly birthDate);
        if (!validDate) errors.Add("請選擇有效的國曆出生日期。");
        else
        {
            try { lunar = CivilLunarCalendar.Convert(birthDate); }
            catch (ArgumentOutOfRangeException exception) { errors.Add(exception.Message.Split(Environment.NewLine)[0]); }
        }
        if (!IsTimePart(input.BirthHour, 23) || !IsTimePart(input.BirthMinute, 59))
            errors.Add("請完整選擇出生時間的時與分，不會預設為 00:00。");
        string country = input.BirthCountry?.Trim() ?? "";
        string city = input.BirthCity?.Trim() ?? "";
        if (country.Length == 0) errors.Add("請填寫出生國家／地區。");
        if (city.Length == 0) errors.Add("請填寫出生城市。");

        List<string> avoid = [];
        foreach (Rune rune in (input.AvoidCharacters ?? "").EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || ",，、;；".Contains(rune.ToString(), StringComparison.Ordinal)) continue;
            if (!Rune.IsLetter(rune)) errors.Add("避諱字請填用字，並以頓號或逗號分隔。");
            else avoid.Add(rune.ToString());
        }
        if (input.NoAvoidanceCharacters && avoid.Count != 0) errors.Add("已確認沒有避諱字時，請清空避諱字內容。");
        if (avoid.Count != 0 && input.AvoidMatchMode is not ("same_character" or "same_sound_tone"))
            errors.Add("有填避諱字時，請選擇只避同字或同字與同音字。");
        string required = input.RequiredCharacter?.Trim() ?? "";
        if (input.NoRequiredCharacter && required.Length != 0) errors.Add("已確認沒有指定字時，請清空指定字內容。");
        if (required.Length != 0)
        {
            if (required.EnumerateRunes().Count() != 1 || !required.EnumerateRunes().All(Rune.IsLetter)) errors.Add("指定字／輩分字目前請填一個字。");
            if (input.RequiredPosition is not ("first" or "last" or "either")) errors.Add("有填指定字時，請選擇名首、名末或不限位置。");
            if (avoid.Contains(required, StringComparer.Ordinal)) errors.Add("同一個字不能同時是指定字與避諱字。");
        }
        if (errors.Count != 0) throw new CaseInputException(errors.Distinct().ToArray());
        return new(surname, birthDate, input.BirthHour + ":" + input.BirthMinute, country, city,
            input.BirthLocationDetail?.Trim() ?? "", avoid.Distinct(StringComparer.Ordinal).ToArray(), required, lunar!);
    }

    private static bool IsTimePart(string? value, int maximum) => value is { Length: 2 }
        && value.All(c => c is >= '0' and <= '9') && int.Parse(value, CultureInfo.InvariantCulture) <= maximum;
}
