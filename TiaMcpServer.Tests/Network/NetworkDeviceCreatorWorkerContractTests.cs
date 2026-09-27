using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkDeviceCreatorWorkerContractTests
{
    [Fact]
    public void Create_UsesTypeItemNameThenDeviceNameForCreateWithItem()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker",
            "Openness",
            "NetworkDeviceCreator.cs");
        var normalizedSource = Regex.Replace(source, @"\s+", " ");
        const string expectedCall =
            @"project\s*\.\s*Devices\s*\.\s*CreateWithItem\s*\(\s*typeIdentifier\s*,\s*deviceItemName\s*,\s*deviceName\s*\)";
        const string reversedCall =
            @"project\s*\.\s*Devices\s*\.\s*CreateWithItem\s*\(\s*typeIdentifier\s*,\s*deviceName\s*,\s*deviceItemName\s*\)";

        Assert.Single(Regex.Matches(normalizedSource, expectedCall));
        Assert.Empty(Regex.Matches(normalizedSource, reversedCall));
    }

    private static string ReadRepositorySource(params string[] pathSegments)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n");
            }

            current = Path.GetDirectoryName(current);
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{Path.Combine(pathSegments)}'.");
    }
}
