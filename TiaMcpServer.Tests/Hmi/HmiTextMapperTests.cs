using Siemens.Engineering;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiTextMapperTests
{
    private static MultilingualText Sample() => new MultilingualText()
        .With("pl-PL", "<body><p>Przycisk</p></body>")
        .With("en-US", "<body><p>Button</p></body>");

    [Fact]
    public void AllProjectLanguagesWhenNoFilter()
    {
        var messages = new List<string>();

        var texts = HmiTextMapper.Map(Sample(), null, messages);

        Assert.Equal(new[] { "en-US", "pl-PL" }, texts.Select(t => t.Culture));
        Assert.Equal("<body><p>Button</p></body>", texts[0].Text); // raw, markup kept
        Assert.Empty(messages);
    }

    [Fact]
    public void FilterNarrowsToOneCulture()
    {
        var texts = HmiTextMapper.Map(Sample(), "PL-pl", new List<string>());

        var only = Assert.Single(texts);
        Assert.Equal("pl-PL", only.Culture);
    }

    [Fact]
    public void NullTextMapsToEmptyArray()
        => Assert.Empty(HmiTextMapper.Map(null, null, new List<string>()));

    [Fact]
    public void NonProjectLanguageFailsItemNamingProjectLanguages()
    {
        var project = ProjectWith().WithLanguages("en-US", "en-US", "pl-PL", "en-US");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiTextMapper.RequireProjectLanguage(project, "de-DE"));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
        Assert.Contains("en-US", ex.Message);
        Assert.Contains("pl-PL", ex.Message);
        HmiTextMapper.RequireProjectLanguage(project, "PL-pl"); // case-insensitive project language passes
    }

    [Fact]
    public void ProjectCulturesAreSorted()
    {
        var project = ProjectWith().WithLanguages("en-US", "en-US", "pl-PL", "en-US");

        Assert.Equal(new[] { "en-US", "pl-PL" }, HmiTextMapper.ProjectCultures(project));
    }
}
