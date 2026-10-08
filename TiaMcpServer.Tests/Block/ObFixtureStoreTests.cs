using System.Xml.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class ObFixtureStoreTests
{
    public static IEnumerable<object[]> Classes() => ObEventClasses.All.Select(cls => new object[] { cls.Name });

    [Theory]
    [MemberData(nameof(Classes))]
    public void EveryClassHasFixtureWithInputSection(string obEventClass)
    {
        var sections = XElement.Parse(ObFixtureStore.LoadSections(obEventClass));

        var input = sections.Elements().Single(section => (string?)section.Attribute("Name") == "Input");
        Assert.NotEmpty(input.Elements());
        Assert.DoesNotContain(sections.Descendants(), element => element.Name.LocalName == "Comment");
    }

    [Theory]
    [InlineData("NoSuchClass")]
    [InlineData("SynchronousCycle")]
    [InlineData("startup")]
    public void UnknownClassIsValidationError(string obEventClass)
    {
        var exception = Assert.Throws<WorkerOperationException>(() => ObFixtureStore.LoadSections(obEventClass));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }
}
