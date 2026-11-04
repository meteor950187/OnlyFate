using System.IO;
using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OnlyFate.Database;
using OnlyFate.Desktop.Views;
using Xunit;

namespace OnlyFate.Desktop.Tests;

// XAML package resources are shared across STA threads; serialize WPF construction.
[Collection("WPF resources")]
public class CaseInputViewTests
{
    private static CaseInput Sample => new()
    {
        Surname = "林", Gender = "female", BirthDate = "2025-07-25", BirthHour = "03", BirthMinute = "09",
        BirthCountry = "台灣", BirthCity = "台北市", BirthLocationDetail = "測試地點",
        AvoidCharacters = "宏、志", AvoidMatchMode = "same_sound_tone", RequiredCharacter = "安", RequiredPosition = "either",
        Remarks = "備註只保存原文。\n第二行。"
    };

    [Fact]
    public void FormReopensEveryFieldWithoutDefaultingBirthFacts() => OnSta(() =>
    {
        CaseInputView view = new();
        Assert.Equal(new CaseInput(), view.ReadInput());
        Assert.False(view.IsDirty);
        view.SetInput(Sample);
        Assert.Equal(Sample, view.ReadInput());
        Assert.False(view.IsDirty);
    });

    [Fact]
    public void EditingMarksUnsavedChangesAndExplicitNoneClearsConflictingValues() => OnSta(() =>
    {
        CaseInputView view = new();
        view.SetInput(Sample);
        ((TextBox)view.FindName("RemarksBox")).Text = "修改後尚未儲存";
        Assert.True(view.IsDirty);
        ((CheckBox)view.FindName("NoAvoidanceBox")).IsChecked = true;
        ((CheckBox)view.FindName("NoRequiredBox")).IsChecked = true;
        CaseInput input = view.ReadInput();
        Assert.True(input.NoAvoidanceCharacters);
        Assert.Equal("", input.AvoidCharacters);
        Assert.Equal("", input.AvoidMatchMode);
        Assert.True(input.NoRequiredCharacter);
        Assert.Equal("", input.RequiredCharacter);
        Assert.Equal("", input.RequiredPosition);
    });

    [Fact]
    public void FormValuesSurviveSqliteSaveAndReopenWithoutDisplayingAWindow() => OnSta(() =>
    {
        string testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OnlyFate.Desktop.Tests")) + Path.DirectorySeparatorChar;
        string directory = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "case.db");
        try
        {
            CaseInputView original = new();
            original.SetInput(Sample);
            StoredCase saved = new CaseRepository(path).Save(original.ReadInput());
            CaseInputView reopened = new();
            reopened.SetInput(new CaseRepository(path).Load(saved.NamingCaseID).Input);
            Assert.Equal(Sample, reopened.ReadInput());
            Assert.False(reopened.IsDirty);
        }
        finally
        {
            string target = Path.GetFullPath(directory);
            if (!target.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    });

    [Fact]
    public void DateTextStillBeingEditedIsDirtyAndReadWithoutWaitingForFocusLoss() => OnSta(() =>
    {
        CaseInputView view = new();
        view.SetInput(Sample);
        DatePicker picker = (DatePicker)view.FindName("BirthDatePicker");
        picker.ApplyTemplate();
        DatePickerTextBox editor = (DatePickerTextBox)picker.Template.FindName("PART_TextBox", picker);
        editor.Text = "2025/7/26";
        Assert.True(view.IsDirty);
        Assert.Equal("2025-07-26", view.ReadInput().BirthDate);
    });

    [Fact]
    public void InvalidDateTextStillBeingEditedIsDirtyWithoutThrowingFromTheCloseGuard() => OnSta(() =>
    {
        CaseInputView view = new();
        view.SetInput(Sample);
        DatePicker picker = (DatePicker)view.FindName("BirthDatePicker");
        picker.ApplyTemplate();
        DatePickerTextBox editor = (DatePickerTextBox)picker.Template.FindName("PART_TextBox", picker);
        editor.Text = "bad-date";
        Assert.True(view.IsDirty);
        Assert.Throws<CaseInputException>(() => view.ReadInput());
    });

    [Fact]
    public void InvalidDateCannotSilentlySavePreviouslySelectedDate() => OnSta(() =>
    {
        CaseInputView view = new();
        view.SetInput(Sample);
        DatePicker picker = (DatePicker)view.FindName("BirthDatePicker");
        picker.Text = "not-a-date";
        Assert.Throws<CaseInputException>(() => view.ReadInput());
        picker.SelectedDate = new DateTime(2025, 7, 25);
        Assert.Equal("2025-07-25", view.ReadInput().BirthDate);
    });

    [Fact]
    public void ActualMcpFixtureDisplaysCivilAndCalculationTimeWithoutShowingWindow() => OnSta(() =>
    {
        using Stream fixture = Assembly.GetExecutingAssembly().GetManifestResourceStream("Step2Fixture")!;
        using JsonDocument json = JsonDocument.Parse(fixture);
        JsonElement chart = json.RootElement.GetProperty("response").GetProperty("structuredContent").GetProperty("data").GetProperty("chart").Clone();
        CaseInput input = Sample with { BirthDate = "2026-06-15", BirthHour = "12", BirthMinute = "00" };
        BirthplaceCandidate place = BirthplaceCatalog.Default.Find("台灣", "台北市").Single();
        var time = BirthTimeResolver.Resolve(input, place.TimeZoneID);
        ChartRunResult result = new(1, 1, "completed", Step2Json.Element(new { time, place }), chart, null);
        MainWindow window = new(); // Never Show: Loaded/DB/WebView2 initialization do not run.
        typeof(MainWindow).GetMethod("ShowChart", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [result]);
        Assert.Contains("庚申", ((TextBlock)window.FindName("ChartPillars")).Text);
        string details = ((TextBlock)window.FindName("ChartDetails")).Text;
        Assert.Contains("2026-06-15 12:00:00", details);
        Assert.Contains("2026-06-15 12:05", details);
        Assert.Contains("Asia/Taipei", details);
        Assert.Contains("城市代表點", details);
        ((Microsoft.Web.WebView2.Wpf.WebView2)window.FindName("ReportPreview")).Dispose();
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Headless form check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
