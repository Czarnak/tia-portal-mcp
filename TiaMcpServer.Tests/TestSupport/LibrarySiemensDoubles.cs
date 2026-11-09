// Offline boundary doubles for library reads and writes. They declare the members of
// reference-stubs/Siemens.Engineering.Base/Library.cs plus test hooks; no Openness assembly is loaded.
// A disposed double throws EngineeringObjectDisposedException on every read, like a deleted Openness object;
// SetAttribute throws so any mutation attempt fails the test.
using System.Collections;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;

namespace Siemens.Engineering
{
    public enum OpenMode { ReadOnly = 0, ReadWrite = 1 }
    public sealed class EngineeringSecurityException : EngineeringException
    {
        public EngineeringSecurityException(string message) : base(message) { }
    }
    public sealed class UsedProduct
    {
        private string? name;
        public Exception? NameFailure { get; set; }
        public string? Name { get => NameFailure is null ? name : throw NameFailure; set => name = value; }
        public string? Version { get; set; }
    }
    public sealed class UsedProductComposition : Library.LibraryComposition<UsedProduct> { }
    public sealed class HistoryEntry
    {
        private string? text;
        public Exception? TextFailure { get; set; }
        public DateTime DateTime { get; set; }
        public string? Text { get => TextFailure is null ? text : throw TextFailure; set => text = value; }
    }
    public sealed class HistoryEntryComposition : Library.LibraryComposition<HistoryEntry> { }
    public sealed partial class TiaPortal
    {
        private readonly Library.GlobalLibraryComposition globalLibraries = new();

        /// <summary>Thrown by <see cref="GlobalLibraries"/> when set, like a lost Portal attachment.</summary>
        public Exception? GlobalLibrariesFailure { get; set; }

        public Library.GlobalLibraryComposition GlobalLibraries
            => GlobalLibrariesFailure is null ? globalLibraries : throw GlobalLibrariesFailure;
    }

    public sealed partial class Project
    {
        public Library.ProjectLibrary? ProjectLibrary { get; set; }
    }

    public abstract partial class ProjectBase
    {
        public string Name { get; set; } = "Project";
    }
}

namespace Siemens.Engineering.Library
{
    public abstract class LibraryDouble : IEngineeringObject
    {
        private IEngineeringObject? parent;

        public bool Disposed { get; set; }

        public IEngineeringObject? Parent
        {
            get => Live(parent);
            set => parent = value;
        }

        protected T Live<T>(T value)
            => Disposed ? throw new EngineeringObjectDisposedException($"{GetType().Name} was deleted.") : value;

        public object GetAttribute(string name) => throw new EngineeringNotSupportedException($"Attribute '{name}' is not modeled.");

        public void SetAttribute(string name, object value)
            => throw new InvalidOperationException($"Library reads must not mutate: SetAttribute('{name}').");
    }

    public class LibraryComposition<T> : IEnumerable<T>
    {
        public List<T> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public IEnumerator<T> GetEnumerator() => EnumerationFailure is null ? Items.GetEnumerator() : throw EnumerationFailure;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public interface ILibrary
    {
        MasterCopySystemFolder MasterCopyFolder { get; }
        LibraryTypeSystemFolder TypeFolder { get; }
    }

    public enum LibraryImportOptions { None = 0, SkipInactiveCultures = 1, ActivateInactiveCultures = 2 }
    public enum TransferResultState { Success = 0, Warning = 1 }
    public sealed class TypeCreateTransferResults
    {
        public LibraryType? CreatedType { get; set; }
        public TransferResultState TransferResultState { get; set; }
    }
    public sealed class VersionCreateTransferResults
    {
        public LibraryTypeVersion? CreatedVersion { get; set; }
        public TransferResultState TransferResultState { get; set; }
    }

    public abstract class LibraryRootDouble : LibraryDouble, ILibrary
    {
        private readonly MasterCopySystemFolder copies;
        private readonly LibraryTypeSystemFolder types;

        protected LibraryRootDouble()
        {
            copies = new MasterCopySystemFolder { Name = "Master copies", Parent = this };
            types = new LibraryTypeSystemFolder { Name = "Types", Parent = this };
        }

        public MasterCopySystemFolder MasterCopyFolder => Live(copies);
        public LibraryTypeSystemFolder TypeFolder => Live(types);
    }

    public class GlobalLibrary : LibraryRootDouble, ITransactionSupport
    {
        private string name = string.Empty;
        private FileInfo? path;

        public Dictionary<string, object?> MetadataValues { get; } = new();
        public Dictionary<string, Exception> MetadataFailures { get; } = new();
        public int MetadataGetCount { get; private set; }
        private T Metadata<T>(string member, T fallback)
        {
            MetadataGetCount++;
            return MetadataFailures.TryGetValue(member, out var failure) ? throw failure
                : Live(MetadataValues.TryGetValue(member, out var value) ? (T)value! : fallback);
        }
        public string? Author => Metadata(nameof(Author), (string?)string.Empty);
        public string? Version => Metadata(nameof(Version), (string?)string.Empty);
        public string? Family => Metadata(nameof(Family), (string?)string.Empty);
        public string? Copyright => Metadata(nameof(Copyright), (string?)string.Empty);
        public string? LastModifiedBy => Metadata(nameof(LastModifiedBy), (string?)string.Empty);
        public DateTime CreationTime => Metadata(nameof(CreationTime), default(DateTime));
        public DateTime LastModified => Metadata(nameof(LastModified), default(DateTime));
        public long Size => Metadata(nameof(Size), 0L);
        private readonly MultilingualText comment = new();
        private readonly LanguageSettings languageSettings = new();
        private readonly UsedProductComposition products = new();
        private readonly HistoryEntryComposition history = new();
        public MultilingualText Comment => Metadata(nameof(Comment), comment);
        public LanguageSettings LanguageSettings => Metadata(nameof(LanguageSettings), languageSettings);
        public UsedProductComposition UsedProducts => Metadata(nameof(UsedProducts), products);
        public HistoryEntryComposition HistoryEntries => Metadata(nameof(HistoryEntries), history);

        public Exception? NameFailure { get; set; }
        public string Name { get => NameFailure is null ? Live(name) : throw NameFailure; set => name = value; }
        public FileInfo? Path { get => Live(path); set => path = value; }

        /// <summary>Thrown by <see cref="IsReadOnly"/> when set.</summary>
        public Exception? ReadOnlyFailure { get; set; }
        public bool IsReadOnly => ReadOnlyFailure is null ? Live(true) : throw ReadOnlyFailure;
        public bool IsWriteProtected => Live(false);
        public bool Modified { get; set; }
        public Exception? ModifiedFailure { get; set; }
        public bool IsModified => ModifiedFailure is null ? Live(Modified) : throw ModifiedFailure;
    }

    public sealed class UserGlobalLibrary : GlobalLibrary
    {
        public Action? SaveAction { get; set; }
        public Action? CloseAction { get; set; }
        public Action<DirectoryInfo, string, LibraryArchivationMode>? ArchiveAction { get; set; }
        public void Save() => (SaveAction ?? throw new InvalidOperationException("Unconfigured library Save."))();
        public void Close() => (CloseAction ?? throw new InvalidOperationException("Unconfigured library Close."))();
        public void Archive(DirectoryInfo directory, string name, LibraryArchivationMode mode)
            => (ArchiveAction ?? throw new InvalidOperationException("Unconfigured library Archive."))(directory, name, mode);
    }

    public enum LibraryArchivationMode { None = 0, Compressed = 1, DiscardRestorableData = 2, DiscardRestorableDataAndCompressed = 3 }

    public sealed class ProjectLibrary : LibraryRootDouble
    {
    }

    public enum GlobalLibraryType { System = 0, Corporate = 1, User = 2 }

    public sealed class GlobalLibraryInfo : LibraryDouble
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Thrown by <see cref="Path"/> when set, like a system library without a usable file path.</summary>
        public Exception? PathFailure { get; set; }
        private FileInfo? path;
        public FileInfo? Path { get => PathFailure is null ? path : throw PathFailure; set => path = value; }
        public bool IsOpen { get; set; }
        public bool IsReadOnly { get; set; }
        public GlobalLibraryType LibraryType { get; set; } = GlobalLibraryType.User;
    }

    /// <summary>
    /// Opened libraries (<see cref="Items"/>) plus available-but-unopened ones (<see cref="Available"/>).
    /// <see cref="GetGlobalLibraryInfos"/> reports both, like V21; it synthesizes a user info for every opened
    /// library not already listed. <see cref="Open"/> throws so a read that tries to open fails the test.
    /// </summary>
    public sealed class GlobalLibraryComposition : IEnumerable<GlobalLibrary>
    {
        public List<GlobalLibrary> Items { get; } = new();
        public List<GlobalLibraryInfo> Available { get; } = new();
        public Func<DirectoryInfo, string, UserGlobalLibrary>? CreateAction { get; set; }
        public Func<FileInfo, OpenMode, UserGlobalLibrary>? OpenAction { get; set; }
        public Func<FileInfo, DirectoryInfo, OpenMode, UserGlobalLibrary>? RetrieveAction { get; set; }
        public T Create<T>(DirectoryInfo directory, string name) where T : class
            => (T)(object)(CreateAction ?? throw new InvalidOperationException("Unconfigured library Create."))(directory, name);
        public UserGlobalLibrary Open(FileInfo path, OpenMode mode)
            => (OpenAction ?? throw new InvalidOperationException("Unconfigured library Open."))(path, mode);
        public UserGlobalLibrary Retrieve(FileInfo path, DirectoryInfo directory, OpenMode mode)
            => (RetrieveAction ?? throw new InvalidOperationException("Unconfigured library Retrieve."))(path, directory, mode);

        /// <summary>Thrown by <see cref="GetGlobalLibraryInfos"/> when set.</summary>
        public Exception? InfosFailure { get; set; }

        /// <summary>False reports only <see cref="Available"/>, so an opened library can lack a matching info.</summary>
        public bool SynthesizeOpenInfos { get; set; } = true;

        public IList<GlobalLibraryInfo> GetGlobalLibraryInfos()
            => InfosFailure is not null ? throw InfosFailure : !SynthesizeOpenInfos ? Available.ToList() : Available.Concat(Items
                .Where(open => !Available.Any(info => info.Name == open.Name
                    && string.Equals(info.Path?.FullName, open.Path?.FullName, StringComparison.OrdinalIgnoreCase)))
                .Select(open => new GlobalLibraryInfo { Name = open.Name, Path = open.Path, IsOpen = true }))
                .ToList();

        public GlobalLibrary Open(GlobalLibraryInfo info)
            => throw new InvalidOperationException($"Library reads must not open: Open('{info.Name}').");

        public IEnumerator<GlobalLibrary> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace Siemens.Engineering.Library.MasterCopies
{
    public interface IMasterCopySource { }
    public enum MasterCopyMode { ThrowIfExists = 0, Rename = 1, Replace = 2 }
    public sealed class MasterCopyComposition : IEnumerable<MasterCopy>
    {
        public List<MasterCopy> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public Func<IMasterCopySource, MasterCopy>? CreateAction { get; set; }
        public Func<MasterCopy, MasterCopy>? CreateFromAction { get; set; }
        public MasterCopy Create(IMasterCopySource source)
            => (CreateAction ?? throw new InvalidOperationException("Unconfigured master-copy Create."))(source);
        public MasterCopy CreateFrom(MasterCopy source)
            => (CreateFromAction ?? throw new InvalidOperationException("Unconfigured master-copy CreateFrom."))(source);
        public IEnumerator<MasterCopy> GetEnumerator() => EnumerationFailure is null ? Items.GetEnumerator() : throw EnumerationFailure;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class MasterCopyUserFolderComposition : IEnumerable<MasterCopyUserFolder>
    {
        public List<MasterCopyUserFolder> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public Func<string, MasterCopyUserFolder>? CreateAction { get; set; }
        public MasterCopyUserFolder Create(string name)
            => (CreateAction ?? throw new InvalidOperationException("Unconfigured master-copy folder Create."))(name);
        public IEnumerator<MasterCopyUserFolder> GetEnumerator() => EnumerationFailure is null ? Items.GetEnumerator() : throw EnumerationFailure;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class MasterCopy : Library.LibraryDouble
    {
        private string name = string.Empty;
        public string Name { get => Live(name); set => name = value; }
        public Action? DeleteAction { get; set; }
        public void Delete() => (DeleteAction ?? throw new InvalidOperationException("Unconfigured master-copy Delete."))();
    }

    public abstract class MasterCopyFolder : Library.LibraryDouble
    {
        private string name = string.Empty;
        public string Name { get => Live(name); set => name = value; }
        public MasterCopyUserFolderComposition Folders { get; } = new();
        public MasterCopyComposition MasterCopies { get; } = new();

        public MasterCopyUserFolder AddFolder(string folderName)
        {
            var folder = new MasterCopyUserFolder { Name = folderName, Parent = this };
            Folders.Items.Add(folder);
            return folder;
        }

        public MasterCopy AddCopy(string copyName)
        {
            var copy = new MasterCopy { Name = copyName, Parent = this };
            MasterCopies.Items.Add(copy);
            return copy;
        }
    }

    public sealed class MasterCopySystemFolder : MasterCopyFolder { }
    public sealed class MasterCopyUserFolder : MasterCopyFolder
    {
        public Action? DeleteAction { get; set; }
        public void Delete() => (DeleteAction ?? throw new InvalidOperationException("Unconfigured master-copy folder Delete."))();
    }
}

namespace Siemens.Engineering.Library.Types
{
    public enum CreateOptions { None = 0, Override = 1 }
    public enum CreateOrReleaseDependenciesMode { DoNotAutomaticallyCreateOrReleaseDependencies = 0, AutomaticallyCreateOrReleaseDependenciesIfRequired = 1 }
    public enum UpdatePathsMode { UpdatePathsInTarget = 0, KeepExistingPathsInTarget = 1, ThrowIfPathsConflict = 2 }
    public enum DeleteUnusedVersionsMode { AutomaticallyDelete = 0, DoNotDelete = 1 }
    public enum StructureConflictResolutionMode { UpdateStructure = 0, RetainStructure = 1, CancelIfStructureConflicts = 2 }
    public enum ForceUpdateMode { SetOnlyHigherUpdatedVersionAsDefault = 0, ForceSetAnyUpdatedVersionAsDefault = 1, NoDefaultVersionChange = 2 }
    public interface IInstanceSearchScope { }
    public sealed class LibraryTypeInstanceInfo : Library.LibraryDouble, IEngineeringService
    {
        public LibraryTypeVersion LibraryTypeVersion { get; set; } = null!;
        public IEngineeringObject LibraryTypeInstance { get; set; } = null!;
    }

    public sealed class LibraryTypeComposition : Library.LibraryComposition<LibraryType>
    {
        public List<(DirectoryInfo Directory, string Name, IEngineeringObject TestEnvironment, Library.LibraryImportOptions Options)> CreateFromDocumentsCalls { get; } = new();
        public Func<DirectoryInfo, string, IEngineeringObject, Library.LibraryImportOptions, Library.TypeCreateTransferResults>? CreateFromDocumentsAction { get; set; }
        public Library.TypeCreateTransferResults CreateFromDocuments(DirectoryInfo directory, string name, IEngineeringObject testEnvironment, Library.LibraryImportOptions options)
        {
            CreateFromDocumentsCalls.Add((directory, name, testEnvironment, options));
            return (CreateFromDocumentsAction ?? throw new InvalidOperationException("Unconfigured type CreateFromDocuments."))(directory, name, testEnvironment, options);
        }
    }

    public sealed class LibraryTypeVersionComposition : Library.LibraryComposition<LibraryTypeVersion>
    {
        public List<(DirectoryInfo Directory, string Name, CreateOptions CreateOptions, Library.LibraryImportOptions ImportOptions)> CreateFromDocumentsCalls { get; } = new();
        public Func<DirectoryInfo, string, CreateOptions, Library.LibraryImportOptions, Library.VersionCreateTransferResults>? CreateFromDocumentsAction { get; set; }
        public Library.VersionCreateTransferResults CreateFromDocuments(DirectoryInfo directory, string name, CreateOptions createOptions, Library.LibraryImportOptions importOptions)
        {
            CreateFromDocumentsCalls.Add((directory, name, createOptions, importOptions));
            return (CreateFromDocumentsAction ?? throw new InvalidOperationException("Unconfigured version CreateFromDocuments."))(directory, name, createOptions, importOptions);
        }
    }
    public sealed class LibraryTypeUserFolderComposition : IEnumerable<LibraryTypeUserFolder>
    {
        public List<LibraryTypeUserFolder> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public Func<string, LibraryTypeUserFolder>? CreateAction { get; set; }
        public LibraryTypeUserFolder Create(string name)
            => (CreateAction ?? throw new InvalidOperationException("Unconfigured type folder Create."))(name);
        public IEnumerator<LibraryTypeUserFolder> GetEnumerator() => EnumerationFailure is null ? Items.GetEnumerator() : throw EnumerationFailure;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public enum LibraryTypeVersionState { InWork = 0, Committed = 1 }

    [Flags]
    public enum ConsistencyStatus
    {
        None = 0, Consistent = 1, DefaultVersionInconsistent = 2, DuplicateVersionInconsistent = 4,
        NonDefaultVersionInstantiation = 8, MultipleVersionsInstantiationInSameDevice = 16,
    }

    public abstract class LibraryTypeFolder : Library.LibraryDouble
    {
        private string name = string.Empty;
        public string Name { get => Live(name); set => name = value; }
        public LibraryTypeUserFolderComposition Folders { get; } = new();
        public LibraryTypeComposition Types { get; } = new();

        public LibraryTypeUserFolder AddFolder(string folderName)
        {
            var folder = new LibraryTypeUserFolder { Name = folderName, Parent = this };
            Folders.Items.Add(folder);
            return folder;
        }

        public LibraryType AddType(string typeName, Guid? guid = null)
        {
            var type = new LibraryType { Name = typeName, Guid = guid ?? Guid.NewGuid(), Parent = this };
            Types.Items.Add(type);
            return type;
        }
    }

    public sealed class LibraryTypeSystemFolder : LibraryTypeFolder { }
    public sealed class LibraryTypeUserFolder : LibraryTypeFolder
    {
        public Action? DeleteAction { get; set; }
        public void Delete() => (DeleteAction ?? throw new InvalidOperationException("Unconfigured type folder Delete."))();
    }

    public sealed class LibraryType : Library.LibraryDouble
    {
        private string name = string.Empty;
        private Guid guid;
        public string Name { get => Live(name); set => name = value; }
        public Guid Guid { get => Live(guid); set => guid = value; }
        public string Namespace { get; set; } = "Fixture";
        public ConsistencyStatus Status { get; set; } = ConsistencyStatus.Consistent;
        public LibraryTypeVersionComposition Versions { get; } = new();
        public Action? DeleteAction { get; set; }
        public List<(Library.ILibrary Library, DeleteUnusedVersionsMode DeleteMode, StructureConflictResolutionMode StructureMode, ForceUpdateMode ForceMode)> UpdateLibraryCalls { get; } = new();
        public Action<Library.ILibrary, DeleteUnusedVersionsMode, StructureConflictResolutionMode, ForceUpdateMode>? UpdateLibraryAction { get; set; }
        public void UpdateLibrary(Library.ILibrary library, DeleteUnusedVersionsMode deleteMode, StructureConflictResolutionMode structureMode, ForceUpdateMode forceMode)
        {
            UpdateLibraryCalls.Add((library, deleteMode, structureMode, forceMode));
            (UpdateLibraryAction ?? throw new InvalidOperationException("Unconfigured type UpdateLibrary."))(library, deleteMode, structureMode, forceMode);
        }
        public void Delete()
        {
            if (DeleteAction is not null) { DeleteAction(); return; }
            if (Parent is LibraryTypeFolder folder) folder.Types.Items.Remove(this);
            Disposed = true;
        }

        public LibraryTypeVersion AddVersion(string number, bool isDefault = false, Guid? versionGuid = null)
        {
            var version = new LibraryTypeVersion
            {
                Guid = versionGuid ?? Guid.NewGuid(), TypeObject = this, VersionNumber = Version.Parse(number),
                State = LibraryTypeVersionState.Committed, IsDefault = isDefault, Parent = this,
            };
            Versions.Items.Add(version);
            return version;
        }
    }

    public class LibraryTypeVersion : Library.LibraryDouble
    {
        private Guid guid;
        public Guid Guid { get => Live(guid); set => guid = value; }
        public LibraryType TypeObject { get; set; } = null!;
        private Version versionNumber = new(0, 0, 1);
        private LibraryTypeVersionState state;
        public Func<Version>? ReadVersionNumber { get; set; }
        public Func<LibraryTypeVersionState>? ReadState { get; set; }
        public Version VersionNumber { get => ReadVersionNumber?.Invoke() ?? versionNumber; set => versionNumber = value; }
        public LibraryTypeVersionState State { get => ReadState?.Invoke() ?? state; set => state = value; }
        public bool IsDefault { get; set; }
        public Library.LibraryComposition<LibraryTypeVersion> Dependencies { get; } = new();
        public Library.LibraryComposition<Library.MasterCopies.MasterCopy> MasterCopiesContainingInstances { get; } = new();
        public Library.LibraryComposition<LibraryTypeVersion> Dependents { get; } = new();
        public List<(CreateOrReleaseDependenciesMode Mode, Version? Number, string? Author, string? Comment)> ReleaseCalls { get; } = new();
        public Action<CreateOrReleaseDependenciesMode, Version?, string?, string?>? ReleaseAction { get; set; }
        public Action? SetAsDefaultAction { get; set; }
        public Action? DiscardAction { get; set; }
        public Action? DeleteAction { get; set; }
        public List<IInstanceSearchScope> FindInstancesCalls { get; } = new();
        public Dictionary<IInstanceSearchScope, IList<LibraryTypeInstanceInfo>> Instances { get; } = new();
        public Func<IInstanceSearchScope, IList<LibraryTypeInstanceInfo>>? FindInstancesAction { get; set; }
        public IList<LibraryTypeInstanceInfo> FindInstances(IInstanceSearchScope scope)
        {
            FindInstancesCalls.Add(scope);
            return FindInstancesAction is not null ? FindInstancesAction(scope)
                : Instances.TryGetValue(scope, out var instances) ? instances : new List<LibraryTypeInstanceInfo>();
        }
        public void Release(CreateOrReleaseDependenciesMode mode, Version? number, string? author, string? comment)
        {
            ReleaseCalls.Add((mode, number, author, comment));
            if (ReleaseAction is not null) { ReleaseAction(mode, number, author, comment); return; }
            if (number is not null) VersionNumber = number;
            State = LibraryTypeVersionState.Committed;
        }
        public void SetAsDefault()
        {
            if (SetAsDefaultAction is not null) { SetAsDefaultAction(); return; }
            foreach (var version in TypeObject.Versions.Items) version.IsDefault = ReferenceEquals(version, this);
        }
        public void Discard()
        {
            if (DiscardAction is not null) { DiscardAction(); return; }
            Remove();
        }
        public void Delete()
        {
            if (DeleteAction is not null) { DeleteAction(); return; }
            Remove();
        }
        private void Remove()
        {
            TypeObject.Versions.Items.Remove(this);
            Disposed = true;
        }
    }
}
