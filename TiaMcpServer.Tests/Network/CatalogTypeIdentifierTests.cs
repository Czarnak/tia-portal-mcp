using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class CatalogTypeIdentifierTests
{
    [Theory]
    [InlineData("OrderNumber:6ES7 510-1DJ01-0AB0/V2.0")]
    [InlineData("GSD:SIEM8139.GSD/M/4")]
    [InlineData("System:Device.S7300")]
    public void CreatableIdentifiersUseSupportedPrefixes(string typeIdentifier)
    {
        Assert.True(CatalogTypeIdentifier.IsCreatable(typeIdentifier));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("i550 cabinet")]
    [InlineData("6ES7 510-1DJ01-0AB0")]
    public void NonCreatableIdentifiersAreRejected(string? typeIdentifier)
    {
        Assert.False(CatalogTypeIdentifier.IsCreatable(typeIdentifier));
    }

    [Fact]
    public void ValidationMessagePointsUserBackToCatalogTypeIdentifierField()
    {
        var message = CatalogTypeIdentifier.BuildValidationMessage("i550 cabinet");

        Assert.Contains("not a creatable", message);
        Assert.Contains("typeIdentifier field", message);
        Assert.Contains("OrderNumber:", message);
    }
}
