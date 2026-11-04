namespace OnlyFate.Desktop;

/// <summary>Navigation stays inside the current immutable report, or clears it to about:blank.</summary>
public static class ReportNavigationPolicy
{
    public static bool Allows(string target, string? reportUri) =>
        target == "about:blank" || (!string.IsNullOrEmpty(reportUri) &&
            (target == reportUri || target.StartsWith(reportUri + "#", StringComparison.Ordinal)));
}
