using System.Diagnostics;
using System.Text.Json;
namespace OnlyFate.Database;

internal sealed class ModelCallRunner(string databasePath)
{
    public async Task<ChartRunResult> RunAsync(long caseId,long revisionId,int stepNumber,string model,ModelConnection connection,JsonElement context,string version,
        Func<string,JsonElement,JsonElement?,IReadOnlyList<string>?,ModelRequest> requestFactory,
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
                var adapter=connection.Adapter;
                var request=adapter.BuildRequest(requestFactory(model.Trim(),context,prior,errors));
                long call=runs.StartModelCall(step,attempt,adapter.Provider,adapter.Operation,model.Trim(),request,version);var watch=Stopwatch.StartNew();
                AiResponse response;
                try { response=await connection.Transport.SendAsync(request,deadline.Token).WaitAsync(deadline.Token); }
                catch(Exception e) {runs.FinishCall(call,e is OperationCanceledException?"cancelled":"failed",null,watch.ElapsedMilliseconds,"TRANSPORT_FAILED");throw;}
                if(response.Body.ValueKind!=JsonValueKind.Object) response=response with{Body=Step2Json.Element(new{rawResponse=response.Body})};
                var result=adapter.ReadResponse(response);
                string? code=result.Outcome switch{ModelOutcome.Completed=>null,ModelOutcome.Refused=>"MODEL_REFUSAL",_=>"API_NOT_COMPLETED"};
                runs.FinishCall(call,code is null?"succeeded":"failed",response.Body,watch.ElapsedMilliseconds,code);runs.ModelUsage(call,result);
                if(result.Outcome==ModelOutcome.NotCompleted)throw new InvalidOperationException($"{adapter.Provider} 未完成（HTTP {response.HttpStatus}）。");
                if(result.Outcome==ModelOutcome.Refused)throw new InvalidOperationException("模型拒絕本次要求，已保存紀錄。");
                string texts=result.OutputText;
                try {using var doc=JsonDocument.Parse(texts);prior=doc.RootElement.Clone();}
                catch(JsonException){prior=Step2Json.Element(new{invalidText=texts});}
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
