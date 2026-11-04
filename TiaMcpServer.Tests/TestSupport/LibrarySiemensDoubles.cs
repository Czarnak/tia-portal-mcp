// Offline boundary doubles for the library_read reader. They declare the read members of
// reference-stubs/Siemens.Engineering.Base/Library.cs plus test hooks; no Openness assembly is loaded.
// A disposed double throws EngineeringObjectDisposedException on every read, like a deleted Openness object;
// SetAttribute throws so any mutation attempt fails the test.
using System.Collections;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;

namespace Siemens.Engineering
{
    public sealed partial class TiaPortal
    {
        public Library.LibraryComposition<Library.GlobalLibrary> GlobalLibraries { get; } = new();
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

    public sealed class LibraryComposition<T> : IEnumerable<T>
    {
        public List<T> Items { get; } = new();
        public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public interface ILibrary
    {
        MasterCopySystemFolder MasterCopyFolder { get; }
        LibraryTypeSystemFolder TypeFolder { get; }
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

    public sealed class GlobalLibrary : LibraryRootDouble
    {
        private string name = string.Empty;
        private FileInfo? path;

        public string Name { get => Live(name); set => name = value; }
        public FileInfo? Path { get => Live(path); set => path = value; }

        /// <summary>Thrown by <see cref="IsReadOnly"/> when set.</summary>
        public Exception? ReadOnlyFailure { get; set; }
        public bool IsReadOnly => ReadOnlyFailure is null ? Live(true) : throw ReadOnlyFailure;
        public bool IsWriteProtected => Live(false);
        public bool IsModified => Live(false);
    }

    public sealed class ProjectLibrary : LibraryRootDouble
    {
    }
}

namespace Siemens.Engineering.Library.MasterCopies
{
    public sealed class MasterCopy : Library.LibraryDouble
    {
        private string name = string.Empty;
        public string Name { get => Live(name); set => name = value; }
    }

    public abstract class MasterCopyFolder : Library.LibraryDouble
    {
        private string name = string.Empty;
        public string Name { get => Live(name); set => name = value; }
        public Library.LibraryComposition<MasterCopyUserFolder> Folders { get; } = new();
        public Library.LibraryComposition<MasterCopy> MasterCopies { get; } = new();

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
    public sealed class MasterCopyUserFolder : MasterCopyFolder { }
}

namespace Siemens.Engineering.Library.Types
{
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
        public Library.LibraryComposition<LibraryTypeUserFolder> Folders { get; } = new();
        public Library.LibraryComposition<LibraryType> Types { get; } = new();

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
    public sealed class LibraryTypeUserFolder : LibraryTypeFolder { }

    public sealed class LibraryType : Library.LibraryDouble
    {
        private string name = string.Empty;
        private Guid guid;
        public string Name { get => Live(name); set => name = value; }
        public Guid Guid { get => Live(guid); set => guid = value; }
        public string Namespace { get; set; } = "Fixture";
        public ConsistencyStatus Status { get; set; } = ConsistencyStatus.Consistent;
        public Library.LibraryComposition<LibraryTypeVersion> Versions { get; } = new();

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

    public sealed class LibraryTypeVersion : Library.LibraryDouble
    {
        private Guid guid;
        public Guid Guid { get => Live(guid); set => guid = value; }
        public LibraryType TypeObject { get; set; } = null!;
        public Version VersionNumber { get; set; } = new(0, 0, 1);
        public LibraryTypeVersionState State { get; set; }
        public bool IsDefault { get; set; }
        public Library.LibraryComposition<LibraryTypeVersion> Dependencies { get; } = new();
        public Library.LibraryComposition<LibraryTypeVersion> Dependents { get; } = new();
    }
}
