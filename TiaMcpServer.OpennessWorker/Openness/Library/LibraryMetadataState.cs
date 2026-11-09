using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

internal sealed class LibraryMetadataState
{
    public Dictionary<string, string?> Comments { get; set; } = new(StringComparer.Ordinal);
    public List<string> ActiveLanguages { get; set; } = new();
    public string? EditingLanguage { get; set; }
    public string? ReferenceLanguage { get; set; }
    internal bool ActiveLanguagesRead { get; set; }
    public static IReadOnlyList<string> Mismatches(LibraryWriteRequestInfo item, LibraryMetadataState before, LibraryMetadataState after)
    {
        var mismatches = new List<string>();
        var expectedActive = new HashSet<string>(before.ActiveLanguages, StringComparer.Ordinal);
        if (item.Operation == "set_languages")
            expectedActive.UnionWith(RequestedLanguages(item));
        if (!expectedActive.SetEquals(after.ActiveLanguages)) mismatches.Add("activeLanguages");
        var editing = item.Operation == "set_languages" && item.EditingLanguage is not null
            ? CultureNameRules.CanonicalName(item.EditingLanguage) : before.EditingLanguage;
        var reference = item.Operation == "set_languages" && item.ReferenceLanguage is not null
            ? CultureNameRules.CanonicalName(item.ReferenceLanguage) : before.ReferenceLanguage;
        if (!string.Equals(editing, after.EditingLanguage, StringComparison.Ordinal)) mismatches.Add("editingLanguage");
        if (!string.Equals(reference, after.ReferenceLanguage, StringComparison.Ordinal)) mismatches.Add("referenceLanguage");

        var requestedComments = item.Operation == "set_comments"
            ? item.Comments!.ToDictionary(x => CultureNameRules.CanonicalName(x.Culture)!, x => x.Text, StringComparer.Ordinal)
            : new Dictionary<string, string?>();
        foreach (var culture in before.Comments.Keys.Union(after.Comments.Keys).Union(requestedComments.Keys))
        {
            before.Comments.TryGetValue(culture, out var oldText);
            after.Comments.TryGetValue(culture, out var actual);
            if (requestedComments.TryGetValue(culture, out var requested))
            {
                if (!string.Equals(requested, actual ?? "", StringComparison.Ordinal)) mismatches.Add("comments:" + culture);
            }
            else if (before.Comments.ContainsKey(culture))
            {
                if (!string.Equals(oldText ?? "", actual ?? "", StringComparison.Ordinal)) mismatches.Add("comments:" + culture);
            }
            else if (!string.IsNullOrEmpty(actual) || before.ActiveLanguages.Contains(culture) || !expectedActive.Contains(culture))
                mismatches.Add("comments:" + culture);
        }
        return mismatches;
    }

    internal static IEnumerable<string> RequestedLanguages(LibraryWriteRequestInfo item)
        => (item.ActivateLanguages ?? new List<string>()).Concat(new[] { item.EditingLanguage, item.ReferenceLanguage }
            .Where(x => x is not null).Select(x => x!))
            .Select(x => CultureNameRules.CanonicalName(x)!).Distinct(StringComparer.Ordinal);
}
