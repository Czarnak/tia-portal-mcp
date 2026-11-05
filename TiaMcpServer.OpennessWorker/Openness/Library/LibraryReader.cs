using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

/// <summary>The ordered rows of one paged library scan and the owner they were read from (a library, or the Portal).</summary>
public sealed record LibrarySnapshot(object Owner, IReadOnlyList<object> Ordered, string Id);

/// <summary>
/// The read-only half of <c>library_read</c>. <c>list_global_libraries</c> lists every available global library of the
/// bound Portal (<c>GetGlobalLibraryInfos</c>, which opens nothing); every other global read resolves only among the
/// libraries already open there, and the project library only on a standalone <see cref="Siemens.Engineering.Project"/>.
/// It never opens, retrieves, saves, closes or changes anything.
/// </summary>
internal static class LibraryReader
{
    private const string ListGlobalLibraries = "list_global_libraries";
    private const int MaxOwnerDepth = 64;
    private const int DefaultLimit = 100;

    /// <summary>Library page snapshots; separate from <see cref="HmiSnapshotStore.Shared"/> so neither evicts the other.</summary>
    public static PageSnapshotStore<LibrarySnapshot> SharedSnapshots { get; } = new(() => DateTime.UtcNow);

    public static LibraryPageInfo Read(
        TiaPortal portal, ProjectBase project, string method, LibraryQueryInfo query, PageSnapshotStore<LibrarySnapshot> snapshots)
    {
        var operation = LibraryReadMethods.OperationFor(method);
        var error = operation is null ? "Unknown library read method." : LibraryReadMethods.Validate(operation, query);
        if (error is null && operation != ListGlobalLibraries
            && LibraryReadMethods.ProjectLibraryReads.Contains(method, StringComparer.Ordinal) != (query.Scope == "project"))
        {
            error = "Method and library scope do not match.";
        }

        if (error is not null)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);
        }

        var held = query.SnapshotId is null ? null : snapshots.Take(project, query.SnapshotId, method);
        if (operation == ListGlobalLibraries)
        {
            // A continuation never rescans, so touch the Portal: a lost attachment fails the item, not every row.
            // Every page re-reads the opened composition, so isOpen and the open-library flags are current.
            var opened = held is null
                ? portal.GlobalLibraries.ToList()
                : HmiReadLog.Guard(() => portal.GlobalLibraries.ToList(), "The Portal of the page snapshot", describe: true);
            return new RowReader(project, query, null, opened).Page(held, portal, method, snapshots, () => portal.GlobalLibraries
                .GetGlobalLibraryInfos()
                .Select(x => (Info: x, Path: InfoPath(x)))
                .OrderBy(x => x.Info.Name, StringComparer.Ordinal)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => (object)x.Info));
        }

        // A continuation pages the library its first page resolved; a closed library fails the item.
        var library = held is null
            ? Resolve(portal, project, query)
            : HmiReadLog.Guard(() => { var owner = (ILibrary)held.Owner; _ = owner.TypeFolder; return owner; },
                "The library of the page snapshot", describe: true);
        var rows = new RowReader(project, query, library);
        IEnumerable<object> Scan() => operation switch
        {
            "inspect_library" => new object[] { library },
            "list_children" => ChildRows(library, query),
            "list_versions" => Type(library, query).Versions.OrderBy(x => x.VersionNumber).ThenBy(x => x.Guid),
            _ => DependencyRows(library, query, rows),
        };

        return rows.Page(held, library, method, snapshots, Scan);
    }

    internal static ILibrary Resolve(TiaPortal portal, ProjectBase project, LibraryQueryInfo query)
    {
        if (query.Scope == "project")
        {
            return (project as Siemens.Engineering.Project)?.ProjectLibrary ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                "The bound project has no project library this read can reach; project-library reads need a standalone project.");
        }

        var wantedPath = query.LibraryPath is null ? null : CanonicalPath(query.LibraryPath);
        var opened = portal.GlobalLibraries.Where(x => Matches(x.Name, x.Path?.FullName, query.LibraryName, wantedPath)).Take(2).ToArray();
        if (opened.Length == 0 && portal.GlobalLibraries.GetGlobalLibraryInfos()
                .Any(x => Matches(x.Name, InfoPath(x), query.LibraryName, wantedPath)))
        {
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                "The global library is available but not open in the bound Portal; open it in TIA Portal first. No library was opened or changed.");
        }

        return Exact(opened, "global library already open in the bound Portal");
    }

    /// <summary>The full path of an available library, or null when TIA Portal reports none it can use (system/corporate libraries may not).</summary>
    internal static string? InfoPath(GlobalLibraryInfo info)
    {
        try
        {
            return FullPath(info.Path?.FullName);
        }
        catch (Exception exception) when (OpennessExceptions.IsRecoverablePropertyFailure(exception) || IsPathFailure(exception))
        {
            return null;
        }
    }

    private static bool IsPathFailure(Exception exception)
        => exception is ArgumentException or NotSupportedException or System.IO.PathTooLongException or System.Security.SecurityException;

    private static string? FullPath(string? path)
    {
        try
        {
            return path is null ? null : System.IO.Path.GetFullPath(path);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            return null;
        }
    }

    /// <summary>The full path of a caller-supplied <c>libraryPath</c>; a malformed one is a validation error, not a worker failure.</summary>
    internal static string CanonicalPath(string libraryPath)
    {
        try
        {
            return System.IO.Path.GetFullPath(libraryPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "libraryPath is not a valid path.");
        }
    }

    private static bool Matches(string name, string? path, string? wantedName, string? wantedCanonicalPath)
    {
        if (wantedName is not null && !string.Equals(name, wantedName, StringComparison.Ordinal)) return false;
        if (wantedCanonicalPath is null) return true;
        var canonical = FullPath(path);
        return canonical is not null && string.Equals(canonical, wantedCanonicalPath, StringComparison.OrdinalIgnoreCase);
    }

    internal static T Exact<T>(IEnumerable<T> matches, string subject)
    {
        var selected = matches.Take(2).ToArray();
        if (selected.Length != 1)
        {
            throw new WorkerOperationException(
                selected.Length == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"Expected exactly one {subject}; found {(selected.Length == 0 ? "none" : "multiple")}. No library was opened or changed.");
        }

        return selected[0];
    }

    internal static LibraryTypeFolder TypeFolder(ILibrary library, List<string>? path)
    {
        LibraryTypeFolder folder = library.TypeFolder;
        foreach (var name in path ?? new List<string>())
        {
            folder = Exact(folder.Folders.Where(x => x.Name == name), "type folder segment");
        }

        return folder;
    }

    internal static MasterCopyFolder CopyFolder(ILibrary library, List<string>? path)
    {
        MasterCopyFolder folder = library.MasterCopyFolder;
        foreach (var name in path ?? new List<string>())
        {
            folder = Exact(folder.Folders.Where(x => x.Name == name), "master-copy folder segment");
        }

        return folder;
    }

    /// <summary>The library that owns <paramref name="type"/> and the user-folder segments below its type root.</summary>
    internal static (ILibrary Library, List<string> Path) Owner(LibraryType type)
    {
        var path = new List<string>();
        var current = type.Parent;
        var depth = 0;
        while (current is LibraryTypeFolder folder)
        {
            if (++depth > MaxOwnerDepth)
            {
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"The library type folder nesting exceeds {MaxOwnerDepth} levels.");
            }

            current = folder.Parent;
            if (current is LibraryTypeFolder) path.Add(folder.Name);
        }

        path.Reverse();
        return (current as ILibrary ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
            "The owning library of the type cannot be established."), path);
    }

    private static LibraryType Type(ILibrary library, LibraryQueryInfo query)
    {
        var guid = Guid.Parse(query.TypeGuid!);
        return Exact(TypeFolder(library, query.FolderPath).Types.Where(x => x.Guid == guid), "library type with that typeGuid");
    }

    /// <summary>Folders before entries, each by ordinal name.</summary>
    private static IEnumerable<object> ChildRows(ILibrary library, LibraryQueryInfo query)
    {
        if (query.FolderKind == "types")
        {
            var folder = TypeFolder(library, query.FolderPath);
            return folder.Folders.OrderBy(x => x.Name, StringComparer.Ordinal).Cast<object>()
                .Concat(folder.Types.OrderBy(x => x.Name, StringComparer.Ordinal));
        }

        var copies = CopyFolder(library, query.FolderPath);
        return copies.Folders.OrderBy(x => x.Name, StringComparer.Ordinal).Cast<object>()
            .Concat(copies.MasterCopies.OrderBy(x => x.Name, StringComparer.Ordinal));
    }

    /// <summary>By owner library and folder path, then type name, then version number.</summary>
    private static IEnumerable<object> DependencyRows(ILibrary library, LibraryQueryInfo query, RowReader rows)
    {
        var versionGuid = Guid.Parse(query.VersionGuid!);
        var version = Exact(Type(library, query).Versions.Where(x => x.Guid == versionGuid), "library type version with that versionGuid");
        var related = query.Direction == "dependents" ? version.Dependents : version.Dependencies;
        return related
            .Select(x => (Version: x, Owner: rows.OwnerKey(x.TypeObject), Type: x.TypeObject.Name))
            .OrderBy(x => x.Owner, StringComparer.Ordinal)
            .ThenBy(x => x.Type, StringComparer.Ordinal)
            .ThenBy(x => x.Version.VersionNumber)
            .Select(x => (object)x.Version);
    }

    /// <summary>Maps rows of one call to entries; caches each owner library's identity so it is read once per call.</summary>
    private sealed class RowReader(ProjectBase project, LibraryQueryInfo query, ILibrary? resolved, IReadOnlyList<GlobalLibrary>? opened = null)
    {
        private readonly Dictionary<ILibrary, LibraryIdentityInfo> owners = new();

        public LibraryPageInfo Page(
            LibrarySnapshot? held, object owner, string method, PageSnapshotStore<LibrarySnapshot> snapshots, Func<IEnumerable<object>> scan)
        {
            var offset = query.Offset ?? 0;
            var ordered = held?.Ordered ?? scan().ToList();
            var (items, total, nextOffset, snapshotId) = SnapshotPager.Page(ordered, offset, query.Limit ?? DefaultLimit, held?.Id,
                rows => snapshots.Put(project, owner, method, id => new LibrarySnapshot(owner, rows, id)));
            return new LibraryPageInfo
            {
                Items = items.Select(row => held is null || IsLive(row) ? Entry(row) : Deleted(row)).ToList(),
                Total = total,
                Offset = offset,
                NextOffset = nextOffset,
                SnapshotId = snapshotId,
                Complete = nextOffset is null,
            };
        }

        public string OwnerKey(LibraryType type)
        {
            var (library, path) = Owner(type);
            var identity = Identity(library);
            return string.Join("\0", new[] { identity.LibraryPath ?? identity.Name }.Concat(path));
        }

        /// <summary>A continuation reads held rows live; a row deleted since the first page fails only that row.</summary>
        private static bool IsLive(object row)
        {
            try
            {
                // A GlobalLibraryInfo row has no native liveness: it is treated as live and re-matched per page.
                _ = row switch
                {
                    LibraryTypeFolder x => x.Name,
                    LibraryType x => x.Name,
                    LibraryTypeVersion x => x.Guid,
                    MasterCopyFolder x => x.Name,
                    MasterCopy x => (object)x.Name,
                    _ => null,
                };
                return true;
            }
            catch (EngineeringObjectDisposedException)
            {
                return false;
            }
        }

        private LibraryEntryInfo Deleted(object row) => new()
        {
            Identity = new LibraryIdentityInfo { Scope = query.Scope ?? "global", Kind = Kind(row), FolderKind = query.FolderKind },
            NativeType = row.GetType().FullName,
            Diagnostics = { $"This {Kind(row)} no longer exists; it was deleted after the first page was read." },
        };

        private static string Kind(object row) => row switch
        {
            ILibrary or GlobalLibraryInfo => "library",
            LibraryTypeFolder or MasterCopyFolder => "folder",
            LibraryType => "type",
            LibraryTypeVersion => "version",
            _ => "masterCopy",
        };

        private LibraryEntryInfo Entry(object row) => row switch
        {
            GlobalLibraryInfo x => InfoEntry(x),
            ILibrary x => LibraryEntry(x),
            LibraryTypeFolder x => Folder(x.Name, "types", x),
            MasterCopyFolder x => Folder(x.Name, "masterCopies", x),
            LibraryType x => TypeEntry(x),
            LibraryTypeVersion x => VersionEntry(x),
            MasterCopy x => new LibraryEntryInfo
            {
                Identity = Child(RootIdentity(), "masterCopies", query.FolderPath ?? new List<string>(), "masterCopy", x.Name),
                NativeType = x.GetType().FullName,
            },
            _ => throw new InvalidOperationException($"Unexpected library row {row.GetType().FullName}."),
        };

        private LibraryEntryInfo Folder(string name, string folderKind, object folder) => new()
        {
            Identity = Child(RootIdentity(), folderKind, (query.FolderPath ?? new List<string>()).Concat(new[] { name }), "folder", name),
            NativeType = folder.GetType().FullName,
        };

        // Folder and master-copy rows of list_children belong to the library the page resolved.
        private LibraryIdentityInfo RootIdentity()
            => Identity(resolved ?? throw new InvalidOperationException("A folder row needs a resolved library."));

        private LibraryEntryInfo LibraryEntry(ILibrary library)
        {
            var entry = new LibraryEntryInfo { Identity = Identity(library), NativeType = library.GetType().FullName };
            if (library is GlobalLibrary global)
            {
                entry.IsReadOnly = Optional(() => global.IsReadOnly, "isReadOnly", entry);
                entry.IsWriteProtected = Optional(() => global.IsWriteProtected, "isWriteProtected", entry);
                entry.IsModified = Optional(() => global.IsModified, "isModified", entry);
            }
            else
            {
                entry.IsModified = Optional(() => project.IsModified, "isModified", entry);
                entry.Diagnostics.Add("isModified reflects the bound project's modified flag, not a project-library flag.");
                entry.Diagnostics.Add("Project-library read-only and write-protection state is not established by this read.");
            }

            return entry;
        }

        /// <summary>
        /// One available global library. An open one is matched to its opened library (canonical path, else exact name)
        /// and read like inspect_library; an unopened one reports only what its info carries. Nothing is opened.
        /// </summary>
        private LibraryEntryInfo InfoEntry(GlobalLibraryInfo info)
        {
            var name = info.Name;
            var path = InfoPath(info);
            var candidates = (opened ?? Array.Empty<GlobalLibrary>())
                .Where(x => path is null ? string.Equals(x.Name, name, StringComparison.Ordinal) : Matches(x.Name, x.Path?.FullName, null, path))
                .Take(2).ToArray();
            var open = candidates.Length == 1 ? candidates[0] : null;
            var entry = open is null ? new LibraryEntryInfo() : LibraryEntry(open);
            entry.Identity = new LibraryIdentityInfo { Scope = "global", Kind = "library", Name = name, LibraryName = name, LibraryPath = path };
            if (path is null) entry.Diagnostics.Add("libraryPath unavailable: TIA Portal reports no usable path for this library.");
            entry.LibraryType = Optional(() => info.LibraryType, "libraryType", entry, x => x switch
            {
                GlobalLibraryType.System => "system",
                GlobalLibraryType.Corporate => "corporate",
                GlobalLibraryType.User => "user",
                _ => null,
            });
            if (open is not null)
            {
                entry.IsOpen = true;
                return entry;
            }

            // The current composition is the authority: a usable path with no opened match is closed, even if a held
            // info still says open. Only an unresolvable match (no path and ambiguous name) falls back to the info.
            entry.IsOpen = path is not null && candidates.Length == 0 ? false : Optional(() => info.IsOpen, "isOpen", entry);
            entry.IsReadOnly = Optional(() => info.IsReadOnly, "isReadOnly", entry);
            entry.Diagnostics.Add(entry.IsOpen == true
                ? "This library is reported open but could not be matched to exactly one opened library, so its open-library flags were not read. No library was opened."
                : "This library is available but not open in the bound Portal, so its contents cannot be read until it is opened in TIA Portal. No library was opened.");
            return entry;
        }

        private LibraryEntryInfo TypeEntry(LibraryType type)
        {
            var (library, path) = Owner(type);
            var identity = Child(Identity(library), "types", path, "type", type.Name);
            identity.TypeGuid = type.Guid.ToString("D");
            return new LibraryEntryInfo
            {
                Identity = identity, Namespace = type.Namespace, State = type.Status.ToString(), NativeType = type.GetType().FullName,
            };
        }

        private LibraryEntryInfo VersionEntry(LibraryTypeVersion version)
        {
            var entry = TypeEntry(version.TypeObject);
            entry.Identity.Kind = "version";
            entry.Identity.VersionGuid = version.Guid.ToString("D");
            entry.VersionNumber = version.VersionNumber.ToString();
            entry.State = version.State.ToString();
            entry.IsDefault = Optional(() => version.IsDefault, "isDefault", entry);
            entry.NativeType = version.GetType().FullName;
            return entry;
        }

        /// <summary>Exact selectors of a library; a project library is named by the binding, never by libraryName/libraryPath.</summary>
        private LibraryIdentityInfo Identity(ILibrary library)
        {
            if (!owners.TryGetValue(library, out var identity))
            {
                var global = library as GlobalLibrary;
                identity = new LibraryIdentityInfo
                {
                    Scope = global is null ? "project" : "global", Kind = "library",
                    Name = global?.Name ?? project.Name, LibraryName = global?.Name, LibraryPath = global?.Path?.FullName,
                };
                owners.Add(library, identity);
            }

            return Copy(identity);
        }

        private static LibraryIdentityInfo Copy(LibraryIdentityInfo x) => new()
        {
            Scope = x.Scope, LibraryName = x.LibraryName, LibraryPath = x.LibraryPath, Kind = x.Kind, Name = x.Name,
        };

        private static LibraryIdentityInfo Child(LibraryIdentityInfo root, string folderKind, IEnumerable<string> path, string kind, string name)
            => new()
            {
                Scope = root.Scope, LibraryName = root.LibraryName, LibraryPath = root.LibraryPath,
                FolderKind = folderKind, FolderPath = path.ToList(), Kind = kind, Name = name,
            };

        /// <summary>An unsupported flag is null with a diagnostic, never false; any other failure fails the read.</summary>
        private static bool? Optional(Func<object?> read, string member, LibraryEntryInfo entry)
        {
            try
            {
                if (read() is bool value) return value;
                entry.Diagnostics.Add(member + " unavailable: the native value is not boolean.");
                return null;
            }
            catch (Exception exception) when (OpennessExceptions.IsRecoverablePropertyFailure(exception))
            {
                entry.Diagnostics.Add(member + " unavailable: " + exception.GetType().Name);
                return null;
            }
        }

        /// <summary>A native enum mapped to its public name; an unmapped value is null with a diagnostic, like an unreadable flag.</summary>
        private static string? Optional<T>(Func<T> read, string member, LibraryEntryInfo entry, Func<T, string?> map)
        {
            try
            {
                var value = read();
                var mapped = map(value);
                if (mapped is null) entry.Diagnostics.Add($"{member} unavailable: unknown native value '{value}'.");
                return mapped;
            }
            catch (Exception exception) when (OpennessExceptions.IsRecoverablePropertyFailure(exception))
            {
                entry.Diagnostics.Add(member + " unavailable: " + exception.GetType().Name);
                return null;
            }
        }
    }
}
