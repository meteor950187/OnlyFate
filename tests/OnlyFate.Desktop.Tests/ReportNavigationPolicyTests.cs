using Xunit;
namespace OnlyFate.Desktop.Tests;

public sealed class ReportNavigationPolicyTests
{
    private const string Report="https://report.onlyfate.local/7/report.html";
    [Theory]
    [InlineData(Report,true)]
    [InlineData(Report+"#birth-analysis",true)]
    [InlineData(Report+"#characters-2",true)]
    [InlineData("about:blank",true)]
    [InlineData(Report+"?redirect=elsewhere#birth-analysis",false)]
    [InlineData("https://report.onlyfate.local/8/report.html#birth-analysis",false)]
    [InlineData("https://report.onlyfate.local.evil/7/report.html#birth-analysis",false)]
    [InlineData("https://evil.test/#"+Report,false)]
    [InlineData("javascript:alert(1)",false)]
    [InlineData("file:///C:/report.html",false)]
    public void OnlyCurrentReportAndItsAnchorsAreAllowed(string target,bool allowed) =>
        Assert.Equal(allowed,ReportNavigationPolicy.Allows(target,Report));

    [Fact] public void NoReportAllowsOnlyClearingThePreview()
    {
        Assert.True(ReportNavigationPolicy.Allows("about:blank",null));
        Assert.False(ReportNavigationPolicy.Allows(Report+"#birth-analysis",null));
        Assert.False(ReportNavigationPolicy.Allows("#birth-analysis",""));
    }
}
