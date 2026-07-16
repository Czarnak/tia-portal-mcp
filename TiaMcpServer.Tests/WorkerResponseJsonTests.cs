using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class WorkerResponseJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void SerializesWarningsWithCamelCaseWireName()
    {
        var response = new WorkerResponse
        {
            Success = true,
            Warnings = new List<string> { "Skipping device X: access denied" }
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);

        Assert.Contains("\"warnings\":[\"Skipping device X: access denied\"]", json);
    }

    [Fact]
    public void OmitsWarningsWhenNoneWereCaptured()
    {
        var response = new WorkerResponse { Success = true };

        var json = JsonSerializer.Serialize(response, JsonOptions);

        Assert.DoesNotContain("\"warnings\"", json);
    }
}
