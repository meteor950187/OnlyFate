using System.Text.Json;
using OnlyFate.Database;
using Xunit;

namespace OnlyFate.Database.Tests;

public class NamingTests
{
    private static CaseInput Input => new() { Surname="林", Gender="male", BirthDate="2026-06-15", BirthHour="12", BirthMinute="00", BirthCountry="台灣", BirthCity="台北市", NoAvoidanceCharacters=true, NoRequiredCharacter=true };
    [Fact] public void LinArithmeticMatchesResearch()
    {
        var grid = NamingEngine.Calculate(8,10,15);
        Assert.Equal(new NameGrids(9,18,25,16,33,"水金土",true), grid);
        Assert.False(NamingEngine.Calculate(8,9,15).Pass);
    }
    [Fact] public void UnknownInputsStopInsteadOfGuessing()
    {
        Assert.Throws<InvalidOperationException>(() => NamingEngine.Generate(Input with { Surname="陳" }, ["金","土"]));
        Assert.Throws<InvalidOperationException>(() => NamingEngine.Generate(Input with { NoAvoidanceCharacters=false, AvoidCharacters="謎", AvoidMatchMode="same_sound_tone" }, ["金","土"]));
        Assert.Throws<InvalidOperationException>(() => NamingEngine.Generate(Input with { NoRequiredCharacter=false, RequiredCharacter="謎", RequiredPosition="first" }, ["金","土"]));
    }
    [Theory] [InlineData("first")] [InlineData("last")] [InlineData("either")]
    public void RequiredCharacterHonorsPosition(string position)
    {
        var result=NamingEngine.Generate(Input with { NoRequiredCharacter=false, RequiredCharacter="鋒", RequiredPosition=position }, ["金","土"]);
        Assert.All(result.Candidates,c=>Assert.True(position=="first" ? c.First.Character=="鋒" : position=="last" ? c.Last.Character=="鋒" : c.First.Character=="鋒" || c.Last.Character=="鋒"));
    }
    [Fact] public void SameSoundUsesAllReadingsAndParserSeparators()
    {
        var result=NamingEngine.Generate(Input with { NoAvoidanceCharacters=false, AvoidCharacters=" 峰；宏，志、", AvoidMatchMode="same_sound_tone" }, ["金","土"]);
        Assert.All(result.Candidates,c=>Assert.DoesNotContain("鋒",c.Name));
        Assert.All(result.Candidates,c=>Assert.DoesNotContain("峰",c.Name));
    }
    [Fact] public void OmittedOptionalIsVisibleAndZeroCandidatesNeverRelaxesRules()
    {
        var result=NamingEngine.Generate(Input with { NoAvoidanceCharacters=false, NoRequiredCharacter=false }, ["火"]);
        Assert.Empty(result.Candidates); Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Coverage,c=>c.Contains("未提供"));
    }
    [Fact] public void LegacyOrBlockedAnalysisCannotOpenTrial()
    {
        Assert.False(AnalysisContract.CanTrialProceed(Step2Json.Element(new { blockingIssues=Array.Empty<string>() })));
        Assert.False(AnalysisContract.CanTrialProceed(Step2Json.Element(new { blockingIssues=new[]{"命局分歧"}, namingElements=new[]{"金"} })));
        Assert.True(AnalysisContract.CanTrialProceed(Step2Json.Element(new { blockingIssues=Array.Empty<string>(), namingElements=new[]{"金","土"} })));
    }
    [Theory] [InlineData("金","金")] [InlineData("金","風")]
    public void InvalidOrDuplicateElementsAreRejected(string a,string b)
    {
        var result=Step2Json.Element(new {summary="摘要",direction="方向",limitations=Array.Empty<string>(),
            evidence=new[]{new{ruleId="R1",factPath="/day",factValue="庚",reason="日干"}},remarks=Array.Empty<object>(),
            namingElements=new[]{a,b},blockingIssues=Array.Empty<string>()});
        Assert.NotEmpty(AnalysisContract.Validate(result,Step2Json.Element(new{day="庚"}),""));
        Assert.False(AnalysisContract.CanTrialProceed(result));
    }
    [Fact] public void ActualBlockingIssueRequiresEmptyElements()
    {
        var result=Step2Json.Element(new {summary="摘要",direction="方向",limitations=Array.Empty<string>(),
            evidence=new[]{new{ruleId="R1",factPath="/day",factValue="庚",reason="日干"}},remarks=Array.Empty<object>(),
            namingElements=new[]{"金"},blockingIssues=new[]{"方向矛盾"}});
        Assert.NotEmpty(AnalysisContract.Validate(result,Step2Json.Element(new{day="庚"}),""));
    }
    [Fact] public void CompletePairsHaveNoRepeatedCharactersOrReadings()
    {
        var result=NamingEngine.Generate(Input,["金","土"]);
        Assert.NotEmpty(result.Candidates);
        Assert.Contains(result.Candidates,c=>c.Name=="林峻鋒");
        Assert.DoesNotContain(result.Candidates,c=>c.Name=="林峰鋒");
        Assert.All(result.Candidates,c=>{
            Assert.True(c.Grids.Pass);
            var chars=new[]{c.Surname,c.First,c.Last};
            Assert.Equal(3,chars.Select(ch=>ch.Character).Distinct().Count());
            Assert.Equal(chars.SelectMany(ch=>ch.Readings).Count(),chars.SelectMany(ch=>ch.Readings).Distinct().Count());
        });
    }
    [Fact] public void PolyphonicAvoidCharacterRejectsSharedReading()
    {
        var result=NamingEngine.Generate(Input with{NoAvoidanceCharacters=false,AvoidCharacters="澄",AvoidMatchMode="same_sound_tone"},["金","土"]);
        Assert.All(result.Candidates,c=>Assert.DoesNotContain("城",c.Name));
    }
    [Fact] public void ExactAvoidanceDoesNotGuessUnknownReading()
    {
        var result=NamingEngine.Generate(Input with{NoAvoidanceCharacters=false,AvoidCharacters="謎",AvoidMatchMode="same_character"},["金","土"]);
        Assert.NotEmpty(result.Candidates);
    }
    [Fact] public void All225ResearchStrokePairsHaveTheSamePassSet()
    {
        var expected=new[]{(9,6),(9,7),(9,16),(10,5),(10,6),(10,7),(10,15),(13,10),(13,16)};
        List<(int,int)> actual=[];
        for(int first=4;first<=18;first++)for(int last=4;last<=18;last++)
            if(NamingEngine.Calculate(8,first,last).Pass)actual.Add((first,last));
        Assert.Equal(expected,actual);
    }
    [Fact] public void ConfigurationsAreVerifiedStrokePairsRegardlessOfLabel()
    {
        var context=PoolTestData.Context(Input);var output=PoolTestData.Selection(context);
        Assert.Empty(NamingContract.Validate(output,context));
        var node=System.Text.Json.Nodes.JsonNode.Parse(output.GetRawText())!;
        node["groups"]![0]!["label"]="999畫不可信標題";
        Assert.Empty(NamingContract.Validate(JsonSerializer.SerializeToElement(node),context));
        node["groups"]![1]=node["groups"]![0]!.DeepClone();
        Assert.NotEmpty(NamingContract.Validate(JsonSerializer.SerializeToElement(node),context));
        var bad=System.Text.Json.Nodes.JsonNode.Parse(context.GetRawText())!;
        bad["pools"]![0]!["firstCharacters"]![0]!["kangxiStrokes"]=999;
        Assert.NotEmpty(NamingContract.Validate(output,JsonSerializer.SerializeToElement(bad)));
    }
}
