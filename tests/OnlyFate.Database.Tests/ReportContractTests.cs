using System.Text.Json;
using System.Text.Json.Nodes;
using OnlyFate.Database;
using Xunit;
namespace OnlyFate.Database.Tests;
public class ReportContractTests
{
 private static JsonElement Context=>Step2Json.Element(new{groups=Enumerable.Range(0,2).Select(i=>new{poolId="pool"+i,entries=Enumerable.Range(0,5).Select(j=>new{candidateId=$"id{i}-{j}",first=new{meaning="寬待",character="宥"},last=new{meaning="金屬聲",character="錚"}})}),forbiddenNames=new[]{"林城鋒"}});
 private static JsonNode Output=>JsonNode.Parse(PoolTestData.Prose(Context).GetRawText())!;
 [Fact] public void CompleteTenEntriesAreValid(){Assert.Empty(ReportContract.Validate(JsonSerializer.SerializeToElement(Output),Context));}
 [Theory][InlineData("other","希望文雅","依字義推薦。")][InlineData("id0-0","保證一生富貴","依字義推薦。")][InlineData("id0-0","希望文雅","五格33大吉。")][InlineData("id0-0","希望文雅","林城鋒也很好。")]
 public void RejectsInvalidProse(string id,string wish,string reason){var n=Output;n["groups"]![0]!["entries"]![0]!["candidateId"]=id;n["groups"]![0]!["entries"]![0]!["wish"]=wish;n["groups"]![0]!["entries"]![0]!["reason"]=reason;Assert.NotEmpty(ReportContract.Validate(JsonSerializer.SerializeToElement(n),Context));}
 [Fact] public void RejectsMalformedAndLongAndMissing(){Assert.NotEmpty(ReportContract.Validate(Step2Json.Element(new{}),Context));var n=Output;n["groups"]![0]!["entries"]![0]!["wish"]=new string('文',81);Assert.NotEmpty(ReportContract.Validate(JsonSerializer.SerializeToElement(n),Context));n=Output;n["groups"]![0]!["entries"]!.AsArray().RemoveAt(0);Assert.NotEmpty(ReportContract.Validate(JsonSerializer.SerializeToElement(n),Context));}
 [Fact] public void RejectsDuplicateAndLongSummary(){var n=Output;n["groups"]![0]!["entries"]![1]=n["groups"]![0]!["entries"]![0]!.DeepClone();Assert.NotEmpty(ReportContract.Validate(JsonSerializer.SerializeToElement(n),Context));n=Output;n["summary"]=new string('文',121);Assert.NotEmpty(ReportContract.Validate(JsonSerializer.SerializeToElement(n),Context));}
 [Fact] public void RejectsCrossGroupIdsAndDuplicateGroups(){var n=Output;n["groups"]![1]!["entries"]![0]!["candidateId"]=n["groups"]![0]!["entries"]![0]!["candidateId"]!.DeepClone();Assert.NotEmpty(ReportContract.Validate(JsonSerializer.SerializeToElement(n),Context));n=Output;n["groups"]![1]!["poolId"]="pool0";Assert.NotEmpty(ReportContract.Validate(JsonSerializer.SerializeToElement(n),Context));}
}
