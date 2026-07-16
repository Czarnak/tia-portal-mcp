using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Siemens.Engineering;
using Siemens.Engineering.HW.HardwareCatalog;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class EquipmentCatalogSearcher
{
    private static readonly string[] FolderCollectionNames =
    {
        "Children",
        "SubFolders",
        "Folders",
        "Groups"
    };

    private static readonly string[] ItemCollectionNames =
    {
        "Items",
        "Entries",
        "CatalogEntries"
    };

    public static List<CatalogEntryInfo> Search(TiaPortal tiaPortal, string query)
    {
        var results = new List<CatalogEntryInfo>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        query = query.Trim();
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddMatchesFromHardwareCatalogFind(tiaPortal, query, results, seenEntries);

        var visited = new HashSet<int>();
        foreach (var catalog in FindCatalogRoots(tiaPortal))
        {
            try
            {
                Traverse(catalog, string.Empty, query, results, seenEntries, visited);
            }
            catch (Exception ex) when (ex is EngineeringException or TargetInvocationException)
            {
                Console.Error.WriteLine($"Skipping hardware catalog root while searching equipment catalog: {ex.Message}");
            }
        }

        return results;
    }

    private static void AddMatchesFromHardwareCatalogFind(
        TiaPortal tiaPortal,
        string query,
        List<CatalogEntryInfo> results,
        HashSet<string> seenEntries)
    {
        try
        {
            // Verified by the V21 device creation reference: HardwareCatalog.Find returns CatalogEntry objects.
            foreach (CatalogEntry entry in tiaPortal.HardwareCatalog.Find(query))
            {
                AddMatch(entry, entry.CatalogPath, query, results, seenEntries);
            }
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping direct hardware catalog search: {ex.Message}");
        }
    }

    private static IEnumerable<object> FindCatalogRoots(TiaPortal tiaPortal)
    {
        // UNVERIFIED SDK CALL: TIA Portal V21 catalog root property name varies across SDK references.
        foreach (var propertyName in new[] { "HardwareCatalog", "GlobalHardwareCatalog" })
        {
            var catalog = OpennessReflection.ReadProperty(tiaPortal, propertyName, $"TIA Portal {propertyName}");
            if (catalog is not null)
            {
                yield return catalog;
            }
        }

        var projects = OpennessReflection.ReadProperty(tiaPortal, "Projects", "TIA Portal projects"); // UNVERIFIED SDK CALL
        foreach (var project in OpennessReflection.Enumerate(projects, "TIA Portal projects"))
        {
            // UNVERIFIED SDK CALL: project-specific hardware catalog availability is not confirmed in V21 stubs.
            foreach (var propertyName in new[] { "HardwareCatalog", "GlobalHardwareCatalog" })
            {
                var catalog = OpennessReflection.ReadProperty(project, propertyName, $"project {propertyName}");
                if (catalog is not null)
                {
                    yield return catalog;
                }
            }
        }
    }

    private static void Traverse(
        object node,
        string catalogPath,
        string query,
        List<CatalogEntryInfo> results,
        HashSet<string> seenEntries,
        HashSet<int> visited)
    {
        if (!visited.Add(RuntimeHelpers.GetHashCode(node)))
        {
            return;
        }

        var nodeName = ReadStringProperty(node, "Name", "catalog node name") ??
            ReadStringProperty(node, "TypeName", "catalog node type name");

        var hasEntryIdentity = HasReadableProperty(node, "TypeIdentifier") ||
            HasReadableProperty(node, "ArticleNumber") ||
            HasReadableProperty(node, "OrderNumber");

        if (hasEntryIdentity)
        {
            AddMatch(node, catalogPath, query, results, seenEntries);
        }

        var childPath = hasEntryIdentity || string.IsNullOrWhiteSpace(nodeName)
            ? catalogPath
            : AppendPath(catalogPath, nodeName!);

        foreach (var propertyName in FolderCollectionNames.Concat(ItemCollectionNames))
        {
            // UNVERIFIED SDK CALL: catalog tree collection property names are reflection-discovered.
            var collection = OpennessReflection.ReadProperty(node, propertyName, $"catalog node {propertyName}");
            foreach (var child in OpennessReflection.Enumerate(collection, $"catalog node {propertyName}"))
            {
                Traverse(child, childPath, query, results, seenEntries, visited);
            }
        }
    }

    private static void AddMatch(
        object candidate,
        string catalogPath,
        string query,
        List<CatalogEntryInfo> results,
        HashSet<string> seenEntries)
    {
        var typeName = ReadStringProperty(candidate, "TypeName", "catalog entry type name") ??
            ReadStringProperty(candidate, "Name", "catalog entry name") ??
            string.Empty;
        var articleNumber = ReadStringProperty(candidate, "ArticleNumber", "catalog entry article number") ??
            ReadStringProperty(candidate, "OrderNumber", "catalog entry order number");
        var description = ReadStringProperty(candidate, "Description", "catalog entry description");

        if (!Contains(typeName, query) &&
            !Contains(articleNumber, query) &&
            !Contains(description, query))
        {
            return;
        }

        var typeIdentifier = ReadStringProperty(candidate, "TypeIdentifier", "catalog entry type identifier") ?? string.Empty;
        if (!CatalogTypeIdentifier.IsCreatable(typeIdentifier))
        {
            return;
        }

        var key = string.Join(
            "|",
            typeIdentifier,
            typeName,
            articleNumber ?? string.Empty,
            ReadStringProperty(candidate, "Version", "catalog entry version") ?? string.Empty);

        if (!seenEntries.Add(key))
        {
            return;
        }

        results.Add(new CatalogEntryInfo
        {
            TypeName = typeName,
            ArticleNumber = articleNumber,
            Version = ReadStringProperty(candidate, "Version", "catalog entry version"),
            TypeIdentifier = typeIdentifier,
            TypeIdentifierNormalized =
                ReadStringProperty(candidate, "TypeIdentifierNormalized", "catalog entry normalized type identifier") ??
                ReadStringProperty(candidate, "NormalizedTypeIdentifier", "catalog entry normalized type identifier"),
            CatalogPath = string.IsNullOrWhiteSpace(catalogPath) ? null : catalogPath,
            Description = description
        });
    }

    private static bool HasReadableProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public) is not null;
    }

    private static string AppendPath(string catalogPath, string name)
    {
        return string.IsNullOrWhiteSpace(catalogPath)
            ? name
            : string.Concat(catalogPath, "/", name);
    }

    private static bool Contains(string? value, string query)
    {
        return value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ReadStringProperty(object instance, string propertyName, string description)
    {
        return OpennessReflection.ReadProperty(instance, propertyName, description)?.ToString();
    }
}
