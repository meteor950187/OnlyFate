using System.IO;
using OnlyFate.Database;
using OnlyFate.Desktop;
using Xunit;
namespace OnlyFate.Desktop.Tests;
public sealed class ExportDestinationPolicyTests
{
 private static string NormalRoot=>Path.GetDirectoryName(Path.GetDirectoryName(DatabaseSchema.DefaultDatabasePath))!;
 public static IEnumerable<object[]> InternalPaths()
 {
  foreach(string root in new[]{NormalRoot,DemoWorkspace.DefaultRoot}) {
   yield return new object[]{root};yield return new object[]{root+"\\"};
   foreach(string folder in new[]{"Reports","Data","WebView2"}) {
    string path=Path.Combine(root,folder,"report.pdf");
    yield return new object[]{path};yield return new object[]{path.ToUpperInvariant()};yield return new object[]{path.Replace('\\','/')};
    yield return new object[]{Path.Combine(root,"unused","..",folder,"report.pdf")};
   }
  }
 }
 [Theory][MemberData(nameof(InternalPaths))]
 public void BothModesRejectEveryInternalRootWithoutWritingFiles(string path)=>Assert.Throws<InvalidOperationException>(()=>ExportDestinationPolicy.Validate(path,_=>null));
 [Fact] public void SafeSiblingIsNotConfusedWithInternalRoot()
 {
  string path=Path.Combine(NormalRoot+"-exports","nested","..","report.pdf");
  Assert.Equal(Path.GetFullPath(path),ExportDestinationPolicy.Validate(path,_=>null));
 }
 [Theory][InlineData(false)][InlineData(true)]
 public void RejectsReparseOnDestinationOrAncestorWithoutCreatingLinks(bool atFile)
 {
  string folder=Path.Combine(Path.GetTempPath(),"OnlyFate-pure-export-check");string path=Path.Combine(folder,"report.pdf");
  Assert.Throws<InvalidOperationException>(()=>ExportDestinationPolicy.Validate(path,p=>p.Equals(atFile?path:folder,StringComparison.OrdinalIgnoreCase)?FileAttributes.ReparsePoint:null));
 }
 [Theory][InlineData("\\\\?\\C:\\export\\report.pdf")][InlineData("\\\\.\\C:\\export\\report.pdf")][InlineData("C:\\export\\report.pdf:stream")][InlineData("C:\\export. \\report.pdf")][InlineData("C:\\ONLYFA~1\\report.pdf")]
 public void RejectsAmbiguousWindowsAliases(string path)=>Assert.Throws<InvalidOperationException>(()=>ExportDestinationPolicy.Validate(path,_=>null));
 [Fact] public void UnknownAncestorAttributesFailClosed()
 {
  string path=Path.Combine(Path.GetTempPath(),"OnlyFate-export-check","report.pdf");
  Assert.Throws<InvalidOperationException>(()=>ExportDestinationPolicy.Validate(path,_=>throw new IOException("synthetic access failure")));
 }
}
