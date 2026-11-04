using System.IO;
using OnlyFate.Database;
namespace OnlyFate.Desktop;

public sealed record ReportArtifactState(bool HtmlReady, bool PdfReady, string HtmlStatus, string PdfStatus);
public static class ReportArtifactInspection
{
    public static ReportArtifactState Read(string databasePath, ReportArtifactService service, long report)
    {
        // Fixed-data/source failure invalidates the whole report. File failure affects only its format.
        new ReportRepository(databasePath).Load(report);
        (bool ready,string status) Inspect(string format) {
            try { return service.GetReady(report,format) is null ? (false,"未就緒") : (true,"已就緒"); }
            catch(InvalidOperationException) { return (false,"檔案異常，請重建"); }
            catch(IOException) { return (false,"檔案無法讀取，請重建"); }
            catch(UnauthorizedAccessException) { return (false,"檔案無法讀取，請確認權限"); }
        }
        var html=Inspect("html");var pdf=Inspect("pdf");
        new ReportRepository(databasePath).Load(report);
        return new(html.ready,pdf.ready,html.status,pdf.status);
    }
}

