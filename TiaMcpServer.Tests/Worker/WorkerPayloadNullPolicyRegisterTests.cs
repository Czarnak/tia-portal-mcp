using System.Reflection;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// Register of the Contracts payload types that still omit null members on the wire, with the
/// reason each has not switched (docs/roadmap/json-contract.md). The register only shrinks:
/// Phase 1b is done — the RequiredMemberEnforcement entries are gone — and Phases 2-3 (the
/// ToolMigration entries) and the batch redesign (the rest) remain. Adding or removing a marker
/// without updating it fails here, so a wire-policy change is always a reviewed change.
/// PlcTypeImportResultInfo, TagMutationResultInfo and BlockMutationResultInfo write nulls and are
/// deliberately unmarked.
/// </summary>
public sealed class WorkerPayloadNullPolicyRegisterTests
{
    private static readonly IReadOnlyDictionary<string, LegacyNullOmissionReason> Expected =
        new Dictionary<string, LegacyNullOmissionReason>(StringComparer.Ordinal);

    [Fact]
    public void ContractsMarkedToOmitNullMembers_AreExactlyTheRegisteredSet()
    {
        var marked = typeof(WorkerJson).Assembly.GetTypes()
            .Select(type => (Type: type, Marker: type.GetCustomAttribute<LegacyNullOmissionAttribute>(inherit: false)))
            .Where(entry => entry.Marker is not null)
            .ToDictionary(entry => entry.Type.Name, entry => entry.Marker!.Reason, StringComparer.Ordinal);

        Assert.Equal(
            Expected.OrderBy(entry => entry.Key, StringComparer.Ordinal),
            marked.OrderBy(entry => entry.Key, StringComparer.Ordinal));
    }
}
