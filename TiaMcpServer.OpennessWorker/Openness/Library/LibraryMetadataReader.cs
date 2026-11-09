using System.Globalization;
using Siemens.Engineering.Library;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

internal static class LibraryMetadataReader
{
    public static LibraryMetadataInfo Read(GlobalLibrary library)
    {
        var result = new LibraryMetadataInfo { Complete = true };
        result.Author = ReadMember("author", () => library.Author, result);
        result.Version = ReadMember("version", () => library.Version, result);
        result.Family = ReadMember("family", () => library.Family, result);
        result.Copyright = ReadMember("copyright", () => library.Copyright, result);
        result.LastModifiedBy = ReadMember("lastModifiedBy", () => library.LastModifiedBy, result);
        result.CreationTime = ReadMember("creationTime", () => library.CreationTime.ToString("O", CultureInfo.InvariantCulture), result);
        result.LastModified = ReadMember("lastModified", () => library.LastModified.ToString("O", CultureInfo.InvariantCulture), result);
        result.SizeKilobytes = ReadMember<long?>("sizeKilobytes", () => library.Size, result);
        result.Comments = ReadMember("comments", () => library.Comment?.Items
            .Select(item => new LibraryCommentInfo { Culture = item.Language?.Culture?.Name, Text = item.Text })
            .OrderBy(item => item.Culture, StringComparer.Ordinal).ToList(), result);
        result.SupportedLanguages = ReadMember("supportedLanguages", () => library.LanguageSettings?.Languages
            .Select(language => language.Culture.Name).OrderBy(culture => culture, StringComparer.Ordinal).ToList(), result);
        result.ActiveLanguages = ReadMember("activeLanguages", () => library.LanguageSettings?.ActiveLanguages
            .Select(language => language.Culture.Name).OrderBy(culture => culture, StringComparer.Ordinal).ToList(), result);
        result.EditingLanguage = ReadMember("editingLanguage", () => library.LanguageSettings?.EditingLanguage?.Culture?.Name, result);
        result.ReferenceLanguage = ReadMember("referenceLanguage", () => library.LanguageSettings?.ReferenceLanguage?.Culture?.Name, result);
        result.UsedProducts = ReadMember("usedProducts", () => ReadCapped(library.UsedProducts, 100, "usedProducts",
            product => new LibraryUsedProductInfo { Name = product.Name, Version = product.Version }, result), result);
        result.History = ReadMember("history", () => ReadCapped(library.HistoryEntries, 50, "history",
            entry => new LibraryHistoryEntryInfo { DateTime = entry.DateTime.ToString("O", CultureInfo.InvariantCulture), Text = entry.Text }, result), result);
        return result;
    }

    private static T ReadMember<T>(string member, Func<T> read, LibraryMetadataInfo result)
    {
        try { return read(); }
        catch (Exception exception) when (OpennessExceptions.IsRecoverablePropertyFailure(exception))
        {
            result.Complete = false;
            result.Diagnostics.Add($"{member} unavailable: {exception.GetType().Name}");
            return default!;
        }
    }

    private static List<TResult>? ReadCapped<T, TResult>(IEnumerable<T>? source, int cap, string member,
        Func<T, TResult> project, LibraryMetadataInfo result)
    {
        if (source is null) return null;
        var items = new List<TResult>();
        foreach (var item in source)
        {
            if (items.Count == cap)
            {
                result.Complete = false;
                result.Diagnostics.Add($"{member} capped at {cap} entries.");
                break;
            }
            items.Add(project(item));
        }
        return items;
    }
}
