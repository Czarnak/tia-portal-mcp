using System.Reflection;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Json;

/// <summary>
/// Register of every Contracts property declared conditional with
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> — the repository's one convention for "this
/// worker-payload member may legitimately be absent" that
/// <c>CanonicalJson.DeserializeWorkerPayload{T}</c>'s required-member rule exempts. The register
/// only grows deliberately: adding (or removing) a conditional member without updating this test
/// fails it, so the change is always reviewed here.
/// </summary>
public sealed class ConditionalMemberRegisterTests
{
    private static readonly IReadOnlyList<string> Expected = new[]
    {
        // Immediate checks appear on new Network mutation outcomes; historical workers may omit them.
        "AddDeviceResultInfo.Verification",
        "ConfigureNetworkDeviceResultInfo.Verification",
        "SubnetLifecycleResultInfo.Verification",
        "DeviceItemInfo.IoDetails",
        "HardwareConfigInfo.Pagination",
        // New ordinary reads report the root collection count; older/paged reads may omit it.
        "HardwareConfigInfo.RootDeviceCount",
        // Structural traversal evidence appears on new ordinary reads; paged/legacy reads may omit it.
        "HardwareConfigInfo.DiscoveryEvidence",
        // Node selectors carry an owner path when it can be captured; older selectors may omit it.
        "NetworkObjectSelectorInfo.InterfacePath",
        // Relationship identities carry owner evidence on new ordinary reads; older reads may omit it.
        "NetworkNodeIdentityInfo.InterfacePath",
        "NetworkNodeIdentityInfo.InterfaceName",
        // Owner path segments carry a type identifier only when that optional scalar can be read.
        "NetworkInterfacePathSegmentInfo.TypeIdentifier",
        "HardwarePaginationInfo.NextCursor",
        // New ordinary reads report relationship evidence; older/legacy reads may omit it.
        "NodeInfo.ConnectionEvidence",
        "SubnetInfo.ConnectionEvidence",
        "WorkerResponse.BlockImportOutcome",
        // Only explicit bind_project inspection actions add this host value; default binding omits it.
        "ProjectBindingResult.Inspection",
    };

    [Fact]
    public void ConditionalMembers_AreExactlyTheRegisteredSet()
    {
        var actual = typeof(WorkerJson).Assembly.GetTypes().Append(typeof(TiaMcpServer.Tools.ProjectBindingResult))
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsWhenWritingNullConditional)
                .Select(property => $"{type.Name}.{property.Name}"))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        var expected = Expected.OrderBy(entry => entry, StringComparer.Ordinal).ToList();

        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var unregistered = actual.Except(expected, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0 && unregistered.Count == 0,
            "TiaMcpServer.Contracts' set of [JsonIgnore(Condition = WhenWritingNull)] members no "
                + "longer matches this register. A new conditional member must be added here (as "
                + "\"TypeName.PropertyName\"), with its description stating when the member "
                + "appears; remove an entry only when its member is no longer conditional. "
                + $"Registered but no longer conditional: [{string.Join(", ", missing)}]. "
                + $"Conditional but not registered: [{string.Join(", ", unregistered)}].");
    }

    private static bool IsWhenWritingNullConditional(PropertyInfo property)
        => property.GetCustomAttributes<JsonIgnoreAttribute>(inherit: false)
            .Any(attribute => attribute.Condition == JsonIgnoreCondition.WhenWritingNull);
}
