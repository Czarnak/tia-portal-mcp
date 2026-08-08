using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class CatalogEntryInfoTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void SerializesDefaultCatalogEntry()
    {
        var entry = new CatalogEntryInfo();

        var roundTripped = RoundTrip(entry);

        Assert.Equal(string.Empty, roundTripped.TypeName);
        Assert.Null(roundTripped.ArticleNumber);
        Assert.Null(roundTripped.Version);
        Assert.Equal(string.Empty, roundTripped.TypeIdentifier);
        Assert.Null(roundTripped.TypeIdentifierNormalized);
        Assert.Null(roundTripped.CatalogPath);
        Assert.Null(roundTripped.Description);
    }

    [Fact]
    public void RoundTripsFullCatalogEntry()
    {
        var entry = new CatalogEntryInfo
        {
            TypeName = "CPU 1516-3 PN/DP",
            ArticleNumber = "6ES7 516-3AN00-0AB0",
            Version = "V1.0",
            TypeIdentifier = "OrderNumber:6ES7 516-3AN00-0AB0/V1.0",
            TypeIdentifierNormalized = "OrderNumber:6ES7516-3AN00-0AB0/V1.0",
            CatalogPath = "Controllers/SIMATIC S7-1500/CPU",
            Description = "SIMATIC S7-1500 CPU"
        };

        var roundTripped = RoundTrip(entry);

        Assert.Equal("CPU 1516-3 PN/DP", roundTripped.TypeName);
        Assert.Equal("6ES7 516-3AN00-0AB0", roundTripped.ArticleNumber);
        Assert.Equal("V1.0", roundTripped.Version);
        Assert.Equal("OrderNumber:6ES7 516-3AN00-0AB0/V1.0", roundTripped.TypeIdentifier);
        Assert.Equal("OrderNumber:6ES7516-3AN00-0AB0/V1.0", roundTripped.TypeIdentifierNormalized);
        Assert.Equal("Controllers/SIMATIC S7-1500/CPU", roundTripped.CatalogPath);
        Assert.Equal("SIMATIC S7-1500 CPU", roundTripped.Description);
    }

    [Fact]
    public void NullableFieldsOmittedWhenNull()
    {
        var entry = new CatalogEntryInfo
        {
            TypeName = "CPU 1516-3 PN/DP",
            TypeIdentifier = "OrderNumber:6ES7 516-3AN00-0AB0/V1.0",
            ArticleNumber = null,
            Version = null,
            TypeIdentifierNormalized = null,
            CatalogPath = null,
            Description = null
        };

        var json = JsonSerializer.Serialize(entry, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<CatalogEntryInfo>(json, JsonOptions);

        Assert.DoesNotContain("articleNumber", json);
        Assert.DoesNotContain("version", json);
        Assert.DoesNotContain("typeIdentifierNormalized", json);
        Assert.DoesNotContain("catalogPath", json);
        Assert.DoesNotContain("description", json);
        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped.ArticleNumber);
        Assert.Null(roundTripped.Version);
        Assert.Null(roundTripped.TypeIdentifierNormalized);
        Assert.Null(roundTripped.CatalogPath);
        Assert.Null(roundTripped.Description);
    }

    private static CatalogEntryInfo RoundTrip(CatalogEntryInfo entry)
    {
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        return JsonSerializer.Deserialize<CatalogEntryInfo>(json, JsonOptions)!;
    }
}
