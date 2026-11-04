using OnlyFate.Database;
using Xunit;
namespace OnlyFate.Database.Tests;
public class PoolCoreTests {
 internal static CaseInput Input => new() {Surname="林",Gender="male",BirthDate="2026-06-15",BirthHour="12",BirthMinute="00",BirthCountry="台灣",BirthCity="台北市",NoAvoidanceCharacters=true,NoRequiredCharacter=true};
 [Fact] public void ExplicitNoneAndOmittedRestrictionsStayDistinct(){var explicitNone=NamingEngine.Generate(Input,["土","金"]);var omitted=NamingEngine.Generate(Input with{NoAvoidanceCharacters=false,NoRequiredCharacter=false},["土","金"]);Assert.Contains(explicitNone.Coverage,c=>c.Contains("已明確確認無避諱"));Assert.Contains(omitted.Coverage,c=>c.Contains("未提供"));Assert.Contains(omitted.Warnings,c=>c.Contains("家族限制"));}
 [Fact] public void UsesVersionedProfiles() { Assert.Equal("naming-pools-trial-v2",NamingEngine.Version); Assert.All(NamingEngine.Generate(Input,["土","金"]).Candidates,c=>Assert.NotNull(c.First.Profile)); }
 [Fact] public void FindsTwoCompletePoolsWithoutCandidateTruncation() {
  var source=NamingEngine.Generate(Input,["土","金"]);var pools=NamingPools.Generate(source.Candidates);NamingPools.RequireTwo(pools);
  Assert.Contains(pools,a=>pools.Any(b=>NamingPools.Compatible(a,b)));
  foreach(var p in pools){Assert.InRange(p.FirstCharacters.Count,4,5);Assert.InRange(p.LastCharacters.Count,4,5);Assert.Equal(p.FirstCharacters.Count*p.LastCharacters.Count,p.PairIds.Count);Assert.All(p.PairIds,id=>Assert.Contains(source.Candidates,c=>c.Id==id));}
 }
 [Fact] public void NewContractAcceptsCompletePoolsAndRejectsMissingPair() {var context=PoolTestData.Context();var output=PoolTestData.Selection(context);Assert.Empty(NamingContract.Validate(output,context));var node=System.Text.Json.Nodes.JsonNode.Parse(context.GetRawText())!;node["pools"]![0]!["pairIds"]!.AsArray().RemoveAt(0);Assert.NotEmpty(NamingContract.Validate(output,System.Text.Json.JsonSerializer.SerializeToElement(node)));}
 [Theory][InlineData("first")][InlineData("last")][InlineData("either")]
 public void RequiredCharacterCannotBeWaivedForFreePool(string position) {var source=NamingEngine.Generate(Input with{NoRequiredCharacter=false,RequiredCharacter="宥",RequiredPosition=position},["土","金"]);Assert.Throws<InvalidOperationException>(()=>NamingPools.RequireTwo(NamingPools.Generate(source.Candidates)));}
}
