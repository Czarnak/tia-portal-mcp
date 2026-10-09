using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.Network;

/// <summary>Bounds delivery copies only; execution plans, worker results and audit item truth remain intact.</summary>
public static class NetworkWritePayloadBudget
{
    private const string Guidance = "Inspect current state with filtered or paged network_read. Use the original exact selector; if unavailable, discover it first. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with network_read.";
    private const int DiagnosticChars = 512;

    public static NetworkGuardedWriteResponse Apply(NetworkGuardedWriteResponse response,
        int maxItemChars = 60000, int maxDocumentChars = 180000)
    {
        var originalChars = Size(response);
        var current = Diagnostics(response, false, originalChars);
        var effects = current.Effects.ToArray();
        var verification = current.Verification;
        var operations = verification?.Operations.ToArray();
        var finalChecks = verification?.FinalChecks;
        var finalOmission = verification?.Omission;

        NetworkGuardedWriteResponse Present(StructuredOperationBatch? batch = null)
        {
            var result = current with { Effects = effects.ToArray(), Batch = batch ?? current.Batch,
                Verification = verification is null ? null : verification with
                { Operations = operations!.ToArray(), FinalChecks = finalChecks!, Omission = finalOmission } };
            return result with { Success = response.Success && !HasOmission(result) };
        }

        void OmitEffect(int index, string reason, int limit)
        {
            effects[index] = effects[index] with { Effect = null,
                Omission = Omission(reason, limit, Size(effects[index].Effect)) };
        }
        void OmitEvidence(int index, string reason, int limit)
        {
            operations![index] = operations[index] with { Evidence = null,
                Omission = Omission(reason, limit, Size(operations[index].Evidence)) };
        }
        void OmitFinal(string reason, int limit)
        {
            finalOmission = Omission(reason, limit, Size(finalChecks));
            finalChecks = Array.Empty<NetworkFinalCheck>();
        }

        // Compact diagnostics in every repeated copy before dropping any whole root.
        // In particular, many individually small check messages can overflow an item.
        if (effects.Any(e => e.Effect is not null && Size(e.Effect) > maxItemChars)
            || operations?.Any(o => o.Evidence is not null && Size(o.Evidence) > maxItemChars) == true
            || finalChecks is { Count: > 0 } && Size(finalChecks) > maxItemChars
            || current.Batch?.Operations.Any(i => i.Result is not null && Size(i.Result) > maxItemChars) == true
            || Size(Present()) > maxDocumentChars)
        {
            current = Diagnostics(current, true, originalChars);
            effects = current.Effects.ToArray();
            verification = current.Verification;
            operations = verification?.Operations.ToArray();
            finalChecks = verification?.FinalChecks;
        }

        for (var i = 0; i < effects.Length; i++)
            if (effects[i].Effect is not null && Size(effects[i].Effect) > maxItemChars)
                OmitEffect(i, StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);
        for (var i = 0; i < (operations?.Length ?? 0); i++)
            if (operations![i].Evidence is not null && Size(operations[i].Evidence) > maxItemChars)
                OmitEvidence(i, StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);
        if (finalChecks is { Count: > 0 } && Size(finalChecks) > maxItemChars)
            OmitFinal(StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);

        if (Size(Present()) > maxDocumentChars)
        {
            // Remove diagnostics whole, never preserve a prefix of rejected worker text.
            // Diagnostics have already been compacted across all copies above.
            // An unpredictable generated result must not displace an earlier representable
            // recovery core. Let the shared helper budget batch values first when the exact
            // protected roots plus worst omission metadata are themselves representable.
            if (current.Batch is { } originalBatch)
            {
                StructuredOperationBatch RestoreEarly(StructuredOperationBatch candidate) => StructuredOperationBatch.FromItems(
                    candidate.Operations.Select((value, index) => value with { Status = originalBatch.Operations[index].Status }).ToArray(),
                    candidate.Truncation);
                var withoutResults = StructuredOperationBatch.FromItems(originalBatch.Operations.Select(value => value with
                {
                    Result = null,
                    Omission = value.Result is null ? value.Omission : Omission(
                        StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars, Size(value.Result))
                }).ToArray(), new(true, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue,
                    originalBatch.Operations.Select(value => value.OperationId).ToArray()));
                if (Size(Present(withoutResults)) <= maxDocumentChars)
                    current = current with { Batch = RestoreEarly(StructuredOperationBatchPayloadBudget.Apply(originalBatch,
                        candidate => Present(RestoreEarly(candidate)), "network_read", _ => Guidance, maxItemChars, maxDocumentChars)) };
            }
            // Unknown verification roots yield before effects; largest values and stable
            // request order decide within each priority. The final batch pass fits the whole document.
            var candidates = effects.Select((e, i) => (Kind: 0, Index: i, Chars: e.Effect is null ? 0 : Size(e.Effect)))
                .Concat((operations ?? []).Select((o, i) => (Kind: 1, Index: i, Chars: o.Evidence is null ? 0 : Size(o.Evidence))))
                .Append((Kind: 2, Index: 0, Chars: finalChecks is { Count: > 0 } ? Size(finalChecks) : 0))
                .Where(c => c.Chars > 0).OrderBy(c => c.Kind == 0 ? 1 : 0)
                .ThenByDescending(c => c.Chars).ThenBy(c => c.Kind).ThenBy(c => c.Index);
            foreach (var candidate in candidates)
            {
                if (Size(Present()) <= maxDocumentChars) break;
                if (candidate.Kind == 0) OmitEffect(candidate.Index, StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
                else if (candidate.Kind == 1) OmitEvidence(candidate.Index, StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
                else OmitFinal(StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
            }
        }

        if (current.Batch is not { } batch) return Present();
        // The generic helper's omitted status describes delivery. Network keeps the trusted
        // execution status and reports incomplete delivery through omission and root success.
        StructuredOperationBatch Restore(StructuredOperationBatch candidate) => StructuredOperationBatch.FromItems(
            candidate.Operations.Select((item, index) => item with { Status = batch.Operations[index].Status }).ToArray(), candidate.Truncation);
        var bounded = StructuredOperationBatchPayloadBudget.Apply(batch, candidate => Present(Restore(candidate)),
            "network_read", _ => Guidance, maxItemChars, maxDocumentChars);
        return Present(Restore(bounded));
    }

    /// <summary>
    /// Admission reserves the actual worst compact wire shape before any binding/worker activity.
    /// Default JSON escaping, all ID copies, 2 guards/item plus a partial-write guard, every
    /// omission and the batch truncation are included. Unbounded selectors live only in omitted
    /// values; they never enter this protected core. Reads retain their existing admission rules.
    /// </summary>
    internal static int MeasureProtectedCore(IReadOnlyList<NetworkOperationRequest> items)
    {
        var omission = Omission(StructuredOperationBatchPayloadBudget.DocumentLimitReason, int.MaxValue, int.MaxValue);
        var longestId = items.OrderByDescending(i => Size(i.OperationId)).First().OperationId;
        var guards = items.SelectMany(item => Enumerable.Repeat(new WriteGuardReport(
            new string('x', 64), "block", item.OperationId, DiagnosticSummary, null), 2))
            .Append(new(new string('x', 64), "block", longestId, DiagnosticSummary, null)).ToArray();
        var batch = new StructuredOperationBatch(items.Count,
            new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
            items.Select(item => new StructuredOperationItem(item.OperationId, item.Operation, "succeeded", null,
                new(new string('x', 64), DiagnosticSummary), omission, StructuredOperationSkipReasons.EarlierOperationFailed,
                Array.Empty<string>())).ToArray(),
            new(true, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, items.Select(i => i.OperationId).ToArray()));
        return Size(new NetworkGuardedWriteResponse("network_write", "1.0", "applied", false,
            new(new string('x', 64), DiagnosticSummary), Array.Empty<string>(), guards,
            items.Select(item => new NetworkWriteEffectPresentation(item.OperationId, null, omission)).ToArray(), batch,
            new(false, items.Select(item => new NetworkOperationVerification(item.OperationId, item.Operation,
                "not_required", null, omission)).ToArray(), Array.Empty<NetworkFinalCheck>(), omission), omission));
    }

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
        { Name = name, Expected = value, Observed = value, Status = "unverified", Message = DiagnosticSummary };
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
            { Status = "unverified", Identity = identity, Checks = checks, Message = DiagnosticSummary };
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
            if (effectChars > 60000 || evidenceChars > 60000 || resultChars > 60000) return int.MaxValue;
            // Evidence occurs both inside the worker result and at verification.operations.
            chars += effectChars + resultChars + evidenceChars;
            // Known observations are included within each separately bounded shape above;
            // aggregate-only padding cannot establish the per-item guarantee.

        }
        finalChecks.Add(Final(NetworkFinalCheck.Write("networkDeviceCountUnchanged"), int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        finalChecks.Add(Final(NetworkFinalCheck.Write("finalHardwareState"), "readable complete inventory"));
        var finalChars = Size(finalChecks);
        if (finalChars > 60000) return int.MaxValue;
        chars += finalChars;
        return chars > int.MaxValue ? int.MaxValue : (int)chars;
    }
    private static NetworkGuardedWriteResponse Diagnostics(NetworkGuardedWriteResponse response, bool compact, int originalChars)
    {
        var omitted = false;
        string Message(string message)
        {
            if ((!compact && Size(message) <= DiagnosticChars) || message == DiagnosticSummary) return message;
            omitted = true;
            return DiagnosticSummary;
        }
        IReadOnlyList<string> Warnings(IReadOnlyList<string> warnings)
        {
            if (warnings.Count == 0 || (!compact && Size(warnings) <= DiagnosticChars)) return warnings;
            omitted = true;
            return Array.Empty<string>();
        }
        NetworkVerificationCheckInfo Check(NetworkVerificationCheckInfo check) => new()
        { Name = check.Name, Expected = check.Expected, Observed = check.Observed, Status = check.Status,
            Message = check.Message is null ? null : Message(check.Message) };
        NetworkMutationVerificationInfo? Evidence(NetworkMutationVerificationInfo? evidence) => evidence is null ? null : new()
        { Status = evidence.Status, Identity = evidence.Identity, Checks = evidence.Checks.Select(Check).ToList(),
            Message = evidence.Message is null ? null : Message(evidence.Message) };
        var verification = response.Verification is null ? null : response.Verification with
        {
            Operations = response.Verification.Operations.Select(o => o with { Evidence = Evidence(o.Evidence) }).ToArray(),
            FinalChecks = response.Verification.FinalChecks.Select(c => c with { Message = c.Message is null ? null : Message(c.Message) }).ToArray()
        };
        var error = response.Error is null ? null : response.Error with { Message = Message(response.Error.Message) };
        var warnings = Warnings(response.Warnings);
        var guards = response.Guards.Select(guard => guard with { Message = Message(guard.Message) }).ToArray();
        var batch = response.Batch is null ? null : StructuredOperationBatch.FromItems(response.Batch.Operations.Select(item => item with
        {
            Result = item.Result is { } result && item.Operation is "add_network_device" or "configure_network_device" or "create_subnet" or "update_subnet" or "delete_subnet"
                ? NetworkPayloadContract.CompactWriteDiagnostics(result, Message, Warnings) : item.Result,
            Failure = item.Failure is null ? null : item.Failure with { Message = Message(item.Failure.Message) },
            Warnings = Warnings(item.Warnings)
        }).ToArray(), response.Batch.Truncation);
        return response with { Error = error, Warnings = warnings, Guards = guards, Batch = batch, Verification = verification,
            Omission = omitted ? Omission("diagnosticDetailsOmitted", compact ? StructuredOperationBatchPayloadBudget.MaxDocumentChars : DiagnosticChars, originalChars) : response.Omission };
    }

    private static bool HasOmission(NetworkGuardedWriteResponse response) => response.Omission is not null
        || response.Effects.Any(e => e.Omission is not null)
        || response.Batch?.Operations.Any(i => i.Omission is not null) == true
        || response.Verification?.Omission is not null
        || response.Verification?.Operations.Any(o => o.Omission is not null) == true;
    private static StructuredOperationOmission Omission(string reason, int limit, int originalChars)
        => new(reason, limit, originalChars, "network_read", Guidance);
    private static int Size<T>(T value) => CanonicalJson.Serialize(value).Length;
}
