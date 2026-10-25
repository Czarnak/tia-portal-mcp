using TiaMcpServer.Contracts.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkVocabularyContractTests
{
    [Fact]
    public void VerificationStatuses_HaveExactWireValues()
    {
        Assert.Equal(
            new[] { "passed", "failed", "unverified", "not_required" },
            new[]
            {
                NetworkVerificationStatuses.Passed,
                NetworkVerificationStatuses.Failed,
                NetworkVerificationStatuses.Unverified,
                NetworkVerificationStatuses.NotRequired,
            });
    }

    [Fact]
    public void SettingKeys_HaveExactWireValues()
    {
        Assert.Equal(
            new[] { "Address", "SubnetMask", "PnDeviceNameAutoGeneration", "PnDeviceName", "Subnet", "IoSystem", "IoSystemSubnet", "IoSystemNumber" },
            new[]
            {
                NetworkSettingKeys.Address,
                NetworkSettingKeys.SubnetMask,
                NetworkSettingKeys.PnDeviceNameAutoGeneration,
                NetworkSettingKeys.PnDeviceName,
                NetworkSettingKeys.Subnet,
                NetworkSettingKeys.IoSystem,
                NetworkSettingKeys.IoSystemSubnet,
                NetworkSettingKeys.IoSystemNumber,
            });
    }

    [Theory]
    [InlineData("Ethernet", "System:Subnet.Ethernet")]
    [InlineData("Profibus", "System:Subnet.Profibus")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("ethernet", null)]
    [InlineData("PROFIBUS", null)]
    [InlineData(" Ethernet", null)]
    [InlineData("Unknown", null)]
    [InlineData("System:Subnet.Ethernet", null)]
    public void TypeIdentifierFor_MapsOnlyApprovedNetworkTypes(string? input, string? expected)
    {
        Assert.Equal("System:Subnet.Ethernet", SubnetLifecycleContract.EthernetTypeIdentifier);
        Assert.Equal("System:Subnet.Profibus", SubnetLifecycleContract.ProfibusTypeIdentifier);
        Assert.Equal(expected, SubnetLifecycleContract.TypeIdentifierFor(input));
    }
}
