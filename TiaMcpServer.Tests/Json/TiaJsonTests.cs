using System.Text.Json;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.Json;

public class TiaJsonTests
{
    [Fact]
    public void Presentation_IsReadOnly()
    {
        Assert.True(TiaJson.Presentation.IsReadOnly);
    }

    [Fact]
    public void Presentation_RejectsMutation()
    {
        Assert.Throws<InvalidOperationException>(() => TiaJson.Presentation.WriteIndented = true);
    }

    [Fact]
    public void Presentation_StillSerializesCamelCaseAndCompact()
    {
        var json = JsonSerializer.Serialize(new { ProjectPath = "C:\\p.ap21" }, TiaJson.Presentation);

        Assert.Equal("{\"projectPath\":\"C:\\\\p.ap21\"}", json);
    }
}
