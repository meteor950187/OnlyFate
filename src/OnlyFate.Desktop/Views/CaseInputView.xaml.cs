using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OnlyFate.Database;

namespace OnlyFate.Desktop.Views;

public partial class CaseInputView : UserControl
{
    private bool suppressChanges;
    private bool invalidDate;
    private CaseInput cleanInput = new();
    public bool IsDirty
    {
        get
        {
            try { return invalidDate || ReadInput() != cleanInput; }
            catch (CaseInputException) { return true; }
        }
    }

    public CaseInputView()
    {
        InitializeComponent();
        BirthHourBox.ItemsSource = new[] { "" }.Concat(Enumerable.Range(0, 24).Select(n => n.ToString("00")));
        BirthMinuteBox.ItemsSource = new[] { "" }.Concat(Enumerable.Range(0, 60).Select(n => n.ToString("00")));
        BirthCountryBox.ItemsSource = new[] { "台灣", "中國", "香港", "澳門", "日本", "新加坡", "馬來西亞", "美國", "加拿大", "澳洲" };
        SetInput(new CaseInput());
    }

    public CaseInput ReadInput()
    {
        return new()
        {
            Surname = SurnameBox.Text, Gender = GenderBox.SelectedValue as string ?? "",
            BirthDate = ReadBirthDate(),
            BirthHour = BirthHourBox.SelectedItem as string ?? "", BirthMinute = BirthMinuteBox.SelectedItem as string ?? "",
            BirthCountry = BirthCountryBox.Text, BirthCity = BirthCityBox.Text, BirthLocationDetail = BirthLocationDetailBox.Text,
            AvoidCharacters = AvoidCharactersBox.Text, AvoidMatchMode = AvoidMatchModeBox.SelectedValue as string ?? "",
            NoAvoidanceCharacters = NoAvoidanceBox.IsChecked == true, RequiredCharacter = RequiredCharacterBox.Text,
            RequiredPosition = RequiredPositionBox.SelectedValue as string ?? "", NoRequiredCharacter = NoRequiredBox.IsChecked == true,
            Remarks = RemarksBox.Text
        };
    }

    private string ReadBirthDate()
    {
        string text = BirthDatePicker.Text;
        if (string.IsNullOrWhiteSpace(text) && !invalidDate) return "";
        if (!DateTime.TryParseExact(text, ["yyyy/M/d", "yyyy/MM/dd", "yyyy-M-d", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime date))
            throw new CaseInputException(["出生日期格式不正確，請填寫西元年／月／日。"]);
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public void SetInput(CaseInput input)
    {
        suppressChanges = true;
        try
        {
            SurnameBox.Text = input.Surname; GenderBox.SelectedValue = input.Gender;
            BirthDatePicker.SelectedDate = DateTime.TryParseExact(input.BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime date) ? date : null;
            BirthHourBox.SelectedItem = input.BirthHour; BirthMinuteBox.SelectedItem = input.BirthMinute;
            BirthCountryBox.Text = input.BirthCountry; UpdateCities(); BirthCityBox.Text = input.BirthCity;
            BirthLocationDetailBox.Text = input.BirthLocationDetail; AvoidCharactersBox.Text = input.AvoidCharacters;
            AvoidMatchModeBox.SelectedValue = input.AvoidMatchMode; NoAvoidanceBox.IsChecked = input.NoAvoidanceCharacters;
            RequiredCharacterBox.Text = input.RequiredCharacter; RequiredPositionBox.SelectedValue = input.RequiredPosition;
            NoRequiredBox.IsChecked = input.NoRequiredCharacter; RemarksBox.Text = input.Remarks;
            invalidDate = false;
            UpdateLunarDate();
            cleanInput = ReadInput();
        }
        finally { suppressChanges = false; }
    }

    private void InvalidBirthDate(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        BirthDatePicker.SelectedDate = null;
        invalidDate = true;
        if (LunarDateText is not null) LunarDateText.Text = "日期格式不正確，請重新選擇國曆日期。";
    }

    private void BirthDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        invalidDate = false;
        UpdateLunarDate();
    }

    private void UpdateLunarDate()
    {
        if (LunarDateText is null) return;
        if (BirthDatePicker.SelectedDate is not DateTime date)
        {
            LunarDateText.Text = "選擇國曆後，自動換算農曆與閏月。";
            return;
        }
        try { LunarDateText.Text = CivilLunarCalendar.Convert(DateOnly.FromDateTime(date)).Display; }
        catch (ArgumentOutOfRangeException exception) { LunarDateText.Text = exception.Message.Split(Environment.NewLine)[0]; }
    }

    private void CountryFocusLeft(object sender, KeyboardFocusChangedEventArgs e) => UpdateCities();

    private void UpdateCities()
    {
        string text = BirthCityBox.Text;
        BirthCityBox.ItemsSource = BirthCountryBox.Text.Trim() is "台灣" or "臺灣"
            ? new[] { "台北市", "新北市", "桃園市", "台中市", "台南市", "高雄市", "基隆市", "新竹市", "嘉義市", "新竹縣", "苗栗縣", "彰化縣", "南投縣", "雲林縣", "嘉義縣", "屏東縣", "宜蘭縣", "花蓮縣", "台東縣", "澎湖縣", "金門縣", "連江縣" }
            : Array.Empty<string>();
        BirthCityBox.Text = text;
    }

    private void ClearAvoidance(object sender, RoutedEventArgs e)
    {
        if (suppressChanges) return;
        AvoidCharactersBox.Clear(); AvoidMatchModeBox.SelectedIndex = 0;
    }

    private void ClearRequiredCharacter(object sender, RoutedEventArgs e)
    {
        if (suppressChanges) return;
        RequiredCharacterBox.Clear(); RequiredPositionBox.SelectedIndex = 0;
    }
}
