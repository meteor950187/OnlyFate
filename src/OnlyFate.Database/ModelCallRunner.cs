using System.Diagnostics;
using System.Text.Json;
namespace OnlyFate.Database;

internal sealed class ModelCallRunner(string databasePath)
{
    public async Task<ChartRunResult> RunAsync(long caseId,long revisionId,int stepNumber,string model,IAnalysisClient client,JsonElement context,string version,
        Func<string,JsonElement,JsonElement?,IReadOnlyList<string>?,JsonElement> requestFactory,
        Func<JsonElement,JsonElement,IReadOnlyList<string>> validate,
        Func<long,long,JsonElement,JsonElement> complete,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(model)||model.Length>150)throw new ArgumentException("請填入可用模型ID。");
        token.ThrowIfCancellationRequested();
        var runs=new ChartRunRepository(databasePath);
        var (run,step)=runs.Start(caseId,revisionId,context,stepNumber);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try {
            JsonElement? prior=null;IReadOnlyList<string>? errors=null;
            for(int attempt=1;attempt<=2;attempt++) {
                deadline.Token.ThrowIfCancellationRequested();
                var request=requestFactory(model.Trim(),context,prior,errors);
                long call=runs.StartModelCall(step,attempt,model.Trim(),request,version);var watch=Stopwatch.StartNew();
                AiResponse response;
                try { response=await client.SendAsync(request,deadline.Token).WaitAsync(deadline.Token); }
                catch(Exception e) {runs.FinishCall(call,e is OperationCanceledException?"cancelled":"failed",null,watch.ElapsedMilliseconds,"TRANSPORT_FAILED");throw;}
                if(response.Body.ValueKind!=JsonValueKind.Object) response=response with{Body=Step2Json.Element(new{rawResponse=response.Body})};
                bool done=response.HttpStatus is >=200 and <300 && response.Body.TryGetProperty("status",out var status)&&status.ValueKind==JsonValueKind.String&&status.GetString()=="completed";
                runs.FinishCall(call,done?"succeeded":"failed",response.Body,watch.ElapsedMilliseconds,done?null:"API_NOT_COMPLETED");runs.ModelUsage(call,response.Body);
                if(!done)throw new InvalidOperationException($"OpenAI 未完成（HTTP {response.HttpStatus}）。");
                List<string> texts=[];
                if(response.Body.TryGetProperty("output",out var items)&&items.ValueKind==JsonValueKind.Array)
                    foreach(var item in items.EnumerateArray()) {
                        if(!item.TryGetProperty("type",out var kind)||kind.GetString()!="message"||!item.TryGetProperty("content",out var contents)||contents.ValueKind!=JsonValueKind.Array)continue;
                        foreach(var content in contents.EnumerateArray()) {
                            if(!content.TryGetProperty("type",out var type))continue;
                            if(type.GetString()=="refusal") {runs.FinishCall(call,"failed",response.Body,watch.ElapsedMilliseconds,"MODEL_REFUSAL");throw new InvalidOperationException("模型拒絕本次要求，已保存紀錄。");}
                            if(type.GetString()=="output_text"&&content.TryGetProperty("text",out var text)&&text.ValueKind==JsonValueKind.String)texts.Add(text.GetString()!);
                        }
                    }
                try {using var doc=JsonDocument.Parse(string.Concat(texts));prior=doc.RootElement.Clone();}
                catch(JsonException){prior=Step2Json.Element(new{invalidText=string.Concat(texts)});}
                errors=validate(prior.Value,context);if(errors.Count!=0)continue;
                deadline.Token.ThrowIfCancellationRequested();
                var output=complete(run,step,prior.Value);
                return new(run,revisionId,"completed",context,output,"試行結果已保存；正式交付資格仍未開放。");
            }
            throw new InvalidOperationException("經一次修正仍未通過檢查："+string.Join("；",errors!));
        } catch(Exception e) {
            bool cancelled=token.IsCancellationRequested||e is CaseConflictException;
            string message=e is CaseConflictException?"案件或上游資料已更新，結果未套用。":token.IsCancellationRequested?"已取消。":e is OperationCanceledException?"超過120秒，已停止等待。":e is InvalidOperationException?e.Message:"未完成，請檢查回應紀錄。";
            runs.End(run,step,cancelled?"cancelled":"failed",null,cancelled?"CANCELLED":stepNumber==3?"ANALYSIS_FAILED":stepNumber==4?"NAMING_FAILED":"REPORT_FAILED",message);
            if(token.IsCancellationRequested)throw new OperationCanceledException(token);
            throw new InvalidOperationException(message,e);
        }
    }
}
