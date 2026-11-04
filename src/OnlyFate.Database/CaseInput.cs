namespace OnlyFate.Database;

public sealed record CaseInput
{
    public string Surname { get; init; } = "";
    public string Gender { get; init; } = "";
    public string BirthDate { get; init; } = "";
    public string BirthHour { get; init; } = "";
    public string BirthMinute { get; init; } = "";
    public string BirthCountry { get; init; } = "";
    public string BirthCity { get; init; } = "";
    public string BirthLocationDetail { get; init; } = "";
    public string AvoidCharacters { get; init; } = "";
    public string AvoidMatchMode { get; init; } = "";
    public bool NoAvoidanceCharacters { get; init; }
    public string RequiredCharacter { get; init; } = "";
    public string RequiredPosition { get; init; } = "";
    public bool NoRequiredCharacter { get; init; }
    public string Remarks { get; init; } = "";
}

public sealed record CaseSummary(long NamingCaseID, long CaseRevisionID, int RevisionNumber, string Title, string UpdatedAtUtc)
{
    public string DisplayTitle => $"#{NamingCaseID}　{Title}";
    public string RevisionLabel => $"第 {RevisionNumber} 版";
}

public sealed record StoredCase(long NamingCaseID, long CaseRevisionID, int RevisionNumber, CaseInput Input);

public sealed class CaseInputException(IReadOnlyList<string> errors) : ArgumentException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class CaseConflictException() : InvalidOperationException("此案件已在其他地方更新，請重新開啟後再儲存。")
{
}
