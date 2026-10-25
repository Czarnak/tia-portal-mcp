using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Safety.Pipeline.GuardedWriteResponseBudget;
using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.Network;

/// <summary>Bounds delivery copies only; execution plans, worker results and audit item truth remain intact.</summary>
public static class NetworkWritePayloadBudget
{
    private const string Guidance = "Inspect current state with filtered or paged network_read. Use the original exact selector; if unavailable, discover it first. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with network_read.";
    private const int DiagnosticChars = 512;
    private static readonly GuardedWriteResponseBudget Budget = new("network_read", Guidance, DiagnosticSummary);

    public static NetworkGuardedWriteResponse Apply(NetworkGuardedWriteResponse response,
        int? maxItemChars = null, int? maxDocumentChars = null)
        => ApplyCore(response,
            maxItemChars ?? StructuredOperationBatchPayloadBudget.MaxItemChars,
            maxDocumentChars ?? StructuredOperationBatchPayloadBudget.MaxDocumentChars);

    private static NetworkGuardedWriteResponse ApplyCore(NetworkGuardedWriteResponse response, int maxItemChars, int maxDocumentChars)
    {
        var originalChars = Size(response);
        var current = Diagnostics(response, false, originalChars, maxDocumentChars);
        var effects = current.Effects.ToArray();
        var verification = current.Verification;
        var operations = verification?.Operations.ToArray();
        var finalChecks = verification?.FinalChecks;
        var finalOmission = verification?.Omission;

        NetworkGuardedWriteResponse Present(StructuredOperationBatch? batch)
        {
            var result = current with { Effects = effects.ToArray(), Batch = batch ?? current.Batch,
                Verification = verification is null ? null : verification with
                { Operations = operations!.ToArray(), FinalChecks = finalChecks!, Omission = finalOmission } };
            return result with { Success = response.Success && !HasOmission(result.Omission,
                effects.Select(e => e.Omission), result.Batch, finalOmission, operations?.Select(o => o.Omission)) };
        }

        void OmitEffect(int index, string reason, int limit)
        {
            effects[index] = effects[index] with { Effect = null,
                Omission = Budget.Omission(reason, limit, Size(effects[index].Effect)) };
        }
        void OmitEvidence(int index, string reason, int limit)
        {
            operations![index] = operations[index] with { Evidence = null,
                Omission = Budget.Omission(reason, limit, Size(operations[index].Evidence)) };
        }
        void OmitFinal(string reason, int limit)
        {
            finalOmission = Budget.Omission(reason, limit, Size(finalChecks));
            finalChecks = Array.Empty<NetworkFinalCheck>();
        }

        // Compact diagnostics in every repeated copy before dropping any whole root.
        // In particular, many individually small check messages can overflow an item.
        if (effects.Any(e => e.Effect is not null && Size(e.Effect) > maxItemChars)
            || operations?.Any(o => o.Evidence is not null && Size(o.Evidence) > maxItemChars) == true
            || finalChecks is { Count: > 0 } && Size(finalChecks) > maxItemChars
            || current.Batch?.Operations.Any(i => i.Result is not null && Size(i.Result) > maxItemChars) == true
            || Size(Present(null)) > maxDocumentChars)
        {
            current = Diagnostics(current, true, originalChars, maxDocumentChars);
            effects = current.Effects.ToArray();
            verification = current.Verification;
            operations = verification?.Operations.ToArray();
            finalChecks = verification?.FinalChecks;
        }

        IEnumerable<GuardedWriteBudgetRoot> ItemRoots()
            => effects.Select((e, i) => new GuardedWriteBudgetRoot(e.Effect is null ? 0 : Size(e.Effect),
                    (reason, limit) => OmitEffect(i, reason, limit)))
                .Concat((operations ?? []).Select((o, i) => new GuardedWriteBudgetRoot(o.Evidence is null ? 0 : Size(o.Evidence),
                    (reason, limit) => OmitEvidence(i, reason, limit))))
                .Append(new(finalChecks is { Count: > 0 } ? Size(finalChecks) : 0, OmitFinal));

        void BeforeDocumentOmission()
        {
            // Generated results must yield before a representable, known recovery core.
            if (current.Batch is not { } originalBatch) return;
            var withoutResults = StructuredOperationBatch.FromItems(originalBatch.Operations.Select(value => value with
            {
                Result = null,
                Omission = value.Result is null ? value.Omission : Budget.Omission(
                    StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars, Size(value.Result))
            }).ToArray(), new(true, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue,
                originalBatch.Operations.Select(value => value.OperationId).ToArray()));
            if (Size(Present(withoutResults)) <= maxDocumentChars)
                current = current with { Batch = Budget.ApplyBatch(originalBatch,
                    candidate => Present(candidate), maxItemChars, maxDocumentChars) };
        }

        // Unknown verification roots yield before effects, with stable size/kind/index ties.
        IEnumerable<GuardedWriteBudgetRoot> DocumentRoots()
            => effects.Select((e, i) => (Kind: 0, Index: i, Chars: e.Effect is null ? 0 : Size(e.Effect)))
                .Concat((operations ?? []).Select((o, i) => (Kind: 1, Index: i, Chars: o.Evidence is null ? 0 : Size(o.Evidence))))
                .Append((Kind: 2, Index: 0, Chars: finalChecks is { Count: > 0 } ? Size(finalChecks) : 0))
                .Where(c => c.Chars > 0).OrderBy(c => c.Kind == 0 ? 1 : 0)
                .ThenByDescending(c => c.Chars).ThenBy(c => c.Kind).ThenBy(c => c.Index)
                .Select(candidate => new GuardedWriteBudgetRoot(candidate.Chars, (reason, limit) =>
                {
                    if (candidate.Kind == 0) OmitEffect(candidate.Index, reason, limit);
                    else if (candidate.Kind == 1) OmitEvidence(candidate.Index, reason, limit);
                    else OmitFinal(reason, limit);
                }));

        return Budget.Apply(Present, () => current.Batch, ItemRoots(), DocumentRoots,
            maxItemChars, maxDocumentChars, BeforeDocumentOmission);
    }

    /// <summary>
    /// Admission reserves the actual worst compact wire shape before any binding/worker activity.
    /// Default JSON escaping, all ID copies, 2 guards/item plus a partial-write guard, every
    /// omission and the batch truncation are included. Unbounded selectors live only in omitted
    /// values; they never enter this protected core. Reads retain their existing admission rules.
    /// </summary>
    internal static int MeasureProtectedCore(IReadOnlyList<NetworkOperationRequest> items)
        => Budget.MeasureProtectedCore(items.Select(i => (i.OperationId, i.Operation)).ToArray(), 2, 1, false,
            core => new NetworkGuardedWriteResponse("network_write", "1.0", WritePhases.Applied, false,
                core.Error, core.Warnings, core.Guards,
                items.Select(i => new NetworkWriteEffectPresentation(i.OperationId, null, core.Omission)).ToArray(), core.Batch,
                new(false, items.Select(i => new NetworkOperationVerification(i.OperationId, i.Operation,
                    NetworkVerificationStatuses.NotRequired, null, core.Omission)).ToArray(), [], core.Omission), core.Omission));

    /// <summary>
    /// Reserves known prepared recovery values, including owner paths in immediate identities
    /// and final check subjects. Public copies are measured independently.
    /// Unpredictable worker-generated identities are not assigned a fabricated length bound.
    /// </summary>
    public static int MeasurePreparedCore(IReadOnlyList<NetworkOperationRequest> items,
        IReadOnlyList<ItemPlan<NetworkWriteEffect>> plans)
    {
        long chars = MeasureProtectedCore(items);
        var finalChecks = new List<NetworkFinalCheck>();
        NetworkVerificationCheckInfo Check(string name, string? value) => new()
        { Name = name, Expected = value, Observed = value, Status = NetworkVerificationStatuses.Unverified, Message = DiagnosticSummary };
        NetworkFinalCheck Observed(NetworkFinalCheck check, string? value, string? observed)
            => check with { Expected = value, Observed = observed, Message = DiagnosticSummary };
        NetworkFinalCheck Final(NetworkFinalCheck check, string? value) => Observed(check, value, value);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var effect = plans[index].Effect!;
            var target = effect.Target;
            var identity = new NetworkMutationIdentityInfo();
            var checks = new List<NetworkVerificationCheckInfo>();
            string? KnownObserved(string field, string? value)
            {
                if (!effect.CurrentSettings.TryGetValue(field, out var prior)
                    || prior.Availability != "available" || prior.Value is null) return value;
                // Both native planner values and detached payload values are possible here.
                // Choose by canonical encoded size, not raw string length: every separately
                // bounded result/immediate/final copy can observe the known prior value.
                var observed = prior.Value.Value switch
                {
                    null => null,
                    string text => text,
                    NetworkEnumValueInfo enumeration => enumeration.Symbol,
                    JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                    JsonElement element when element.ValueKind == JsonValueKind.Null => null,
                    JsonElement element when prior.Value.Kind == "enum"
                        => CanonicalJson.Deserialize<NetworkEnumValueInfo>(element.GetRawText()).Symbol,
                    JsonElement element => element.GetRawText(),
                    var scalar => Convert.ToString(scalar, System.Globalization.CultureInfo.InvariantCulture)
                };
                return Size(observed) > Size(value) ? observed : value;
            }
            void Add(string name, string? value)
            {
                var check = Check(name, value);
                check.Observed = KnownObserved(name, value);
                checks.Add(check);
            }
            if (item.Operation == "configure_network_device")
            {
                identity.DeviceName = target.DeviceName!;
                identity.NodeId = target.NodeId!;
                identity.InterfacePath = NetworkWritePlanner.ClonePath(target.InterfacePath!);
                identity.InterfaceName = item.Target?.InterfaceName;
                foreach (var setting in effect.RequestedSettings) Add(setting.Key, setting.Value);
            }
            else if (item.Operation == "add_network_device")
            {
                identity.DeviceName = item.DeviceName!;
                identity.DeviceItemName = item.DeviceItemName ?? item.DeviceName!;
                foreach (var setting in effect.RequestedSettings) Add(setting.Key, setting.Value);
                foreach (var setting in effect.RequestedSettings)
                    finalChecks.Add(Final(NetworkFinalCheck.Device(item.DeviceName!, identity.DeviceItemName, setting.Key), setting.Value));
            }
            else
            {
                // Creation has no observed persistent ID yet. Reserve every known requested
                // value; the generated identity still passes strict projection and delivery.
                var subnetId = target.SubnetId ?? "";
                identity.SubnetId = subnetId;
                Add("networkDeviceCountUnchanged", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (item.Operation == "delete_subnet")
                {
                    Add("subnetAbsent", "true"); Add("affectedNodesPreserved", "true"); Add("affectedConnectionsRemoved", "true");
                    finalChecks.Add(Final(NetworkFinalCheck.Subnet(subnetId, "absent"), "true"));
                }
                else
                {
                    Add("subnetIdentity", subnetId);
                    finalChecks.Add(Final(NetworkFinalCheck.Subnet(subnetId, "exists"), "true"));
                    foreach (var setting in effect.RequestedSettings)
                    {
                        Add(setting.Key, setting.Value);
                        finalChecks.Add(Observed(NetworkFinalCheck.Subnet(subnetId, setting.Key), setting.Value, KnownObserved(setting.Key, setting.Value)));
                    }
                }
            }
            foreach (var node in effect.AffectedNodes)
            {
                finalChecks.Add(Final(NetworkFinalCheck.Node(node, "exists"), "true"));
                if (item.Operation == "delete_subnet")
                    finalChecks.Add(Final(NetworkFinalCheck.Node(node, "removedSubnet", target.SubnetId), "true"));
                if (item.Operation == "configure_network_device")
                    foreach (var setting in effect.RequestedSettings)
                    {
                        // A skipped key's final check expects its prior value instead of the requested one.
                        var known = KnownObserved(setting.Key, setting.Value);
                        finalChecks.Add(Observed(NetworkFinalCheck.Node(node, setting.Key), known, known));
                    }
            }
            // Unknown outcomes also retain their operation identity in an immediateEvidence check.
            finalChecks.Add(Final(NetworkFinalCheck.Operation(item.OperationId, "immediateEvidence"), "available"));
            var evidence = new NetworkMutationVerificationInfo
            { Status = NetworkVerificationStatuses.Unverified, Identity = identity, Checks = checks, Message = DiagnosticSummary };
            var skipped = effect.RequestedSettings.ToDictionary(p => p.Key, _ => DiagnosticSummary);
            object result = item.Operation switch
            {
                "configure_network_device" => new ConfigureNetworkDeviceResultInfo
                { DeviceName = target.DeviceName!, AppliedSettings = new(effect.RequestedSettings), SkippedSettings = skipped, Verification = evidence },
                "add_network_device" => new AddDeviceResultInfo
                { DeviceName = item.DeviceName!, RootItemName = item.DeviceItemName ?? item.DeviceName!, TypeIdentifier = item.TypeIdentifier!, Verification = evidence },
                _ => new SubnetLifecycleResultInfo
                { SubnetId = target.SubnetId ?? "", Name = effect.RequestedSettings.GetValueOrDefault("Name") ?? target.SubnetName ?? "", NetworkDeviceCount = int.MaxValue, Verification = evidence }
            };
            var effectChars = Size(effect);
            var evidenceChars = Size(evidence);
            var resultChars = Size(result);
            var itemLimit = StructuredOperationBatchPayloadBudget.MaxItemChars;
            if (effectChars > itemLimit || evidenceChars > itemLimit || resultChars > itemLimit) return int.MaxValue;
            // Evidence occurs both inside the worker result and at verification.operations.
            chars += effectChars + resultChars + evidenceChars;
            // Known observations are included within each separately bounded shape above;
            // aggregate-only padding cannot establish the per-item guarantee.

        }
        finalChecks.Add(Final(NetworkFinalCheck.Write("networkDeviceCountUnchanged"), int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        finalChecks.Add(Final(NetworkFinalCheck.Write("finalHardwareState"), "readable complete inventory"));
        var finalChars = Size(finalChecks);
        if (finalChars > StructuredOperationBatchPayloadBudget.MaxItemChars) return int.MaxValue;
        chars += finalChars;
        return chars > int.MaxValue ? int.MaxValue : (int)chars;
    }
    private static NetworkGuardedWriteResponse Diagnostics(NetworkGuardedWriteResponse response, bool compact, int originalChars, int maxDocumentChars)
    {
        var diagnostics = Budget.Diagnostics(omitWarnings: true, messageLimit: compact ? null : DiagnosticChars);
        NetworkVerificationCheckInfo Check(NetworkVerificationCheckInfo check) => new()
        { Name = check.Name, Expected = check.Expected, Observed = check.Observed, Status = check.Status,
            Message = check.Message is null ? null : diagnostics.Message(check.Message) };
        NetworkMutationVerificationInfo? Evidence(NetworkMutationVerificationInfo? evidence) => evidence is null ? null : new()
        { Status = evidence.Status, Identity = evidence.Identity, Checks = evidence.Checks.Select(Check).ToList(),
            Message = evidence.Message is null ? null : diagnostics.Message(evidence.Message) };
        var verification = response.Verification is null ? null : response.Verification with
        {
            Operations = response.Verification.Operations.Select(o => o with { Evidence = Evidence(o.Evidence) }).ToArray(),
            FinalChecks = response.Verification.FinalChecks.Select(c => c with { Message = c.Message is null ? null : diagnostics.Message(c.Message) }).ToArray()
        };
        var error = diagnostics.Error(response.Error);
        var warnings = diagnostics.Warnings(response.Warnings);
        var guards = diagnostics.Guards(response.Guards);
        var batch = diagnostics.Batch(response.Batch, item =>
            item.Result is { } result && item.Operation is "add_network_device" or "configure_network_device" or "create_subnet" or "update_subnet" or "delete_subnet"
                ? NetworkPayloadContract.CompactWriteDiagnostics(result, diagnostics.Message, diagnostics.Warnings) : item.Result);
        return response with { Error = error, Warnings = warnings, Guards = guards, Batch = batch, Verification = verification,
            Omission = Budget.DiagnosticOmission(diagnostics, response.Omission, compact ? maxDocumentChars : DiagnosticChars, originalChars) };
    }

}
