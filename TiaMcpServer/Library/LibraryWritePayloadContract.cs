using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

public static class LibraryWritePayloadContract
{
    public static StructuredOperationItem Project(LibraryWriteItem item, WorkerCallResult result)
    {
        try
        {
            if (!result.Success)
            {
                if (result.LibraryWriteOutcome is not { } outcome)
                    return StructuredOperationItem.Failed(item,
                        result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                        result.Error ?? "The library write failed.", result.Warnings);
                ValidateIdentity(outcome.Library);
                ValidateType(outcome.Type);
                ValidateMetadataChange(outcome.MetadataChange, item);
                return new(item.OperationId, item.Operation, OperationBatchStatus.Failed, CanonicalJson.ToElement(outcome),
                    new(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                        result.Error ?? "The library write failed."), null, null, result.Warnings);
            }
            var (_, _, element) = CanonicalJson.NormalizeWorkerPayload<LibraryWriteResultInfo>(result.Payload, value =>
            {
                if (item.Operation is "set_comments" or "set_languages" && value.MetadataChange is null) throw new JsonException();
                ValidateIdentity(value.Library);
                ValidateIdentity(value.Target);
                ValidateType(value.Type);
                ValidateMetadataChange(value.MetadataChange, item, successful: true);
                LibraryPayloadContract.ValidateMetadata(value.Metadata);
            });
            return new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, element, null, null, null, result.Warnings);
        }
        catch (JsonException)
        {
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                "The library worker payload did not match its declared result contract and was rejected.", result.Warnings);
        }
    }

    internal static void ValidateIdentity(LibraryIdentityInfo? identity)
    {
        if (identity is not null && (identity.Scope is null || identity.Kind is null || identity.Name is null
            || identity.FolderPath is null || identity.FolderPath.Any(segment => segment is null))) throw new JsonException();
    }

    internal static void ValidateType(LibraryTypeResultInfo? type)
    {
        if (type is not null && (type.TypeName is null || type.TypeGuid is null || type.FolderPath is null || type.InstancePaths is null || type.DeletedVersions is null
            || type.FolderPath.Concat(type.InstancePaths).Concat(type.DeletedVersions).Any(value => value is null))) throw new JsonException();
    }

    internal static void ValidateMetadataChange(LibraryMetadataChangeInfo? change, LibraryWriteItem? item = null, bool successful = false)
    {
        if (change is null) return;
        if (change.Comments is { } comments)
        {
            if (comments.Count == 0 || comments.Any(comment => comment is null || !CanonicalCulture(comment.Culture))
                || comments.Select(comment => comment.Culture).Distinct(StringComparer.Ordinal).Count() != comments.Count
                || change.ActivatedLanguages is not null || change.EditingLanguage is not null || change.ReferenceLanguage is not null)
                throw new JsonException();
        }
        if (change.ActivatedLanguages is { } languages
            && (languages.Any(culture => !CanonicalCulture(culture)) || languages.Distinct(StringComparer.Ordinal).Count() != languages.Count))
            throw new JsonException();
        foreach (var language in new[] { change.EditingLanguage, change.ReferenceLanguage })
            if (language is not null && (language.Before is not null && !CanonicalCulture(language.Before)
                || language.After is not null && !CanonicalCulture(language.After))) throw new JsonException();
        if (item is null) return;
        if (item.Operation == "set_comments")
        {
            if (change.Comments is null || item.Comments is null || change.Comments.Count != item.Comments.Count
                || change.ActivatedLanguages is not null || change.EditingLanguage is not null || change.ReferenceLanguage is not null)
                throw new JsonException();
            for (var i = 0; i < item.Comments.Count; i++)
                if (change.Comments[i].Culture != CultureNameRules.CanonicalName(item.Comments[i].Culture)
                    || successful && change.Comments[i].After != item.Comments[i].Text) throw new JsonException();
        }
        else if (item.Operation == "set_languages")
        {
            var requestedLanguages = (item.ActivateLanguages ?? [])
                .Concat(new[] { item.EditingLanguage, item.ReferenceLanguage }.OfType<string>())
                .Select(culture => CultureNameRules.CanonicalName(culture)!).ToHashSet(StringComparer.Ordinal);
            if (successful && change.ActivatedLanguages?.Any(culture => !requestedLanguages.Contains(culture)) == true)
                throw new JsonException();
            if (change.Comments is not null || (change.EditingLanguage is not null) != (item.EditingLanguage is not null)
                || (change.ReferenceLanguage is not null) != (item.ReferenceLanguage is not null)
                || successful && (change.ActivatedLanguages is null
                    || item.EditingLanguage is not null && change.EditingLanguage!.After != CultureNameRules.CanonicalName(item.EditingLanguage)
                    || item.ReferenceLanguage is not null && change.ReferenceLanguage!.After != CultureNameRules.CanonicalName(item.ReferenceLanguage)))
                throw new JsonException();
        }
        else throw new JsonException();
    }

    private static bool CanonicalCulture(string? culture)
        => culture is not null && CultureNameRules.CanonicalName(culture) == culture;

    internal static void ValidateEnvelopeOutcome(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object) return;
        var members = envelope.EnumerateObject()
            .Where(p => string.Equals(p.Name, "libraryWriteOutcome", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (members.Length == 0) return;
        // Validate raw evidence before the permissive legacy envelope reader normalizes it.
        if (members.Length != 1 || members[0].Name != "libraryWriteOutcome") throw new JsonException();
        if (members[0].Value.ValueKind != JsonValueKind.Null)
        {
            var outcome = CanonicalJson.DeserializeWorkerPayload<LibraryWriteOutcomeInfo>(members[0].Value.GetRawText());
            ValidateIdentity(outcome.Library);
            ValidateType(outcome.Type);
            ValidateMetadataChange(outcome.MetadataChange);
        }
    }
}
