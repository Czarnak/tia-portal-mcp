// Compile-only V21 declarations (library subset). No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering
{
    public enum OpenMode { ReadOnly = 0, ReadWrite = 1 }
}

namespace Siemens.Engineering.Library
{
    public enum LibraryImportOptions { None = 0, SkipInactiveCultures = 1, ActivateInactiveCultures = 2 }
    public enum TransferResultState { Success = 0, Warning = 1 }
    public abstract class TypeCreateTransferResults
    {
        public Types.LibraryType CreatedType { get => throw new global::System.NotSupportedException(); }
        public TransferResultState TransferResultState { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class VersionCreateTransferResults
    {
        public Types.LibraryTypeVersion CreatedVersion { get => throw new global::System.NotSupportedException(); }
        public TransferResultState TransferResultState { get => throw new global::System.NotSupportedException(); }
    }
    public enum LibraryArchivationMode
    {
        None = 0,
        Compressed = 1,
        DiscardRestorableData = 2,
        DiscardRestorableDataAndCompressed = 3
    }
    public class UserGlobalLibrary : global::Siemens.Engineering.Library.GlobalLibrary
    {
        public void Save() => throw new global::System.NotSupportedException();
        public void Close() => throw new global::System.NotSupportedException();
        public void Archive(global::System.IO.DirectoryInfo targetDirectory, string targetName, LibraryArchivationMode archivationMode) => throw new global::System.NotSupportedException();
    }
    public interface ILibrary
    {
        global::Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder MasterCopyFolder { get; }
        global::Siemens.Engineering.Library.Types.LibraryTypeSystemFolder TypeFolder { get; }
    }
    public abstract class GlobalLibrary : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.ITransactionSupport, global::Siemens.Engineering.Library.ILibrary
    {
        public string Author { get => throw new global::System.NotSupportedException(); }
        public string Version { get => throw new global::System.NotSupportedException(); }
        public string Family { get => throw new global::System.NotSupportedException(); }
        public string Copyright { get => throw new global::System.NotSupportedException(); }
        public string LastModifiedBy { get => throw new global::System.NotSupportedException(); }
        public global::System.DateTime CreationTime { get => throw new global::System.NotSupportedException(); }
        public global::System.DateTime LastModified { get => throw new global::System.NotSupportedException(); }
        public long Size { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText Comment { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.LanguageSettings LanguageSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.UsedProductComposition UsedProducts { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HistoryEntryComposition HistoryEntries { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder MasterCopyFolder { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryTypeSystemFolder TypeFolder { get => throw new global::System.NotSupportedException(); }
        public bool IsModified { get => throw new global::System.NotSupportedException(); }
        public bool IsReadOnly { get => throw new global::System.NotSupportedException(); }
        public bool IsWriteProtected { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public global::System.IO.FileInfo Path { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class ProjectLibrary : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.Library.ILibrary
    {
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder MasterCopyFolder { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryTypeSystemFolder TypeFolder { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum GlobalLibraryType : int
    {
        System = 0,
        Corporate = 1,
        User = 2,
    }
    public abstract class GlobalLibraryInfo : global::Siemens.Engineering.IEngineeringObject
    {
        public bool IsOpen { get => throw new global::System.NotSupportedException(); }
        public bool IsReadOnly { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.GlobalLibraryType LibraryType { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public global::System.IO.FileInfo Path { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class GlobalLibraryComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.GlobalLibrary>
    {
        public T Create<T>(global::System.IO.DirectoryInfo targetDirectory, string name) where T : class => throw new global::System.NotSupportedException();
        public UserGlobalLibrary Open(global::System.IO.FileInfo path, global::Siemens.Engineering.OpenMode openMode) => throw new global::System.NotSupportedException();
        public UserGlobalLibrary Retrieve(global::System.IO.FileInfo sourcePath, global::System.IO.DirectoryInfo targetDirectory, global::Siemens.Engineering.OpenMode openMode) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.Library.GlobalLibraryInfo> GetGlobalLibraryInfos() => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.GlobalLibrary> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}

namespace Siemens.Engineering.Library.MasterCopies
{
    public interface IMasterCopySource { }
    public enum MasterCopyMode { ThrowIfExists = 0, Rename = 1, Replace = 2 }
    public abstract class MasterCopy : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public void Delete() => throw new global::System.NotSupportedException();
        public string Name { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class MasterCopyFolder : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopyUserFolderComposition Folders { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopyComposition MasterCopies { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class MasterCopySystemFolder : global::Siemens.Engineering.Library.MasterCopies.MasterCopyFolder
    {
    }
    public abstract class MasterCopyUserFolder : global::Siemens.Engineering.Library.MasterCopies.MasterCopyFolder
    {
        public void Delete() => throw new global::System.NotSupportedException();
    }
    public abstract class MasterCopyUserFolderComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.MasterCopies.MasterCopyUserFolder>
    {
        public MasterCopyUserFolder Create(string name) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.MasterCopies.MasterCopyUserFolder> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class MasterCopyAssociation : global::System.Collections.Generic.IEnumerable<MasterCopy>
    {
        public global::System.Collections.Generic.IEnumerator<MasterCopy> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class MasterCopyComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.MasterCopies.MasterCopy>
    {
        public MasterCopy Create(IMasterCopySource masterCopyObject) => throw new global::System.NotSupportedException();
        public MasterCopy CreateFrom(MasterCopy sourceMasterCopy) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.MasterCopies.MasterCopy> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
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
    public abstract class LibraryTypeInstanceInfo : global::Siemens.Engineering.IEngineeringService
    {
        public LibraryTypeVersion LibraryTypeVersion { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.IEngineeringObject LibraryTypeInstance { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum ConsistencyStatus : int
    {
        None = 0,
        Consistent = 1,
        DefaultVersionInconsistent = 2,
        DuplicateVersionInconsistent = 4,
        NonDefaultVersionInstantiation = 8,
        MultipleVersionsInstantiationInSameDevice = 16,
    }
    public abstract class LibraryType : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public void Delete() => throw new global::System.NotSupportedException();
        public void UpdateLibrary(global::Siemens.Engineering.Library.ILibrary targetLibrary, DeleteUnusedVersionsMode deleteUnusedVersionsMode, StructureConflictResolutionMode structureConflictResolutionMode, ForceUpdateMode forceUpdateMode) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.Library.Types.LibraryTypeVersionComposition Versions { get => throw new global::System.NotSupportedException(); }
        public global::System.Guid Guid { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public string Namespace { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.ConsistencyStatus Status { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.IEngineeringObject Parent { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class LibraryTypeComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.Types.LibraryType>
    {
        public global::Siemens.Engineering.Library.TypeCreateTransferResults CreateFromDocuments(global::System.IO.DirectoryInfo directoryInfo, string fileNameWithoutExtension, global::Siemens.Engineering.IEngineeringObject testEnvironment, global::Siemens.Engineering.Library.LibraryImportOptions libraryImportOptions) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.Types.LibraryType> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeFolder : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.Library.Types.LibraryTypeUserFolderComposition Folders { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryTypeComposition Types { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.IEngineeringObject Parent { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class LibraryTypeSystemFolder : global::Siemens.Engineering.Library.Types.LibraryTypeFolder
    {
    }
    public abstract class LibraryTypeUserFolder : global::Siemens.Engineering.Library.Types.LibraryTypeFolder
    {
        public void Delete() => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeUserFolderComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.Types.LibraryTypeUserFolder>
    {
        public LibraryTypeUserFolder Create(string name) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.Types.LibraryTypeUserFolder> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeVersion : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public void Release(CreateOrReleaseDependenciesMode dependenciesMode, global::System.Version versionNumber, string author, string comment) => throw new global::System.NotSupportedException();
        public void SetAsDefault() => throw new global::System.NotSupportedException();
        public void Discard() => throw new global::System.NotSupportedException();
        public void Delete() => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<LibraryTypeInstanceInfo> FindInstances(IInstanceSearchScope searchScope) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopyAssociation MasterCopiesContainingInstances { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryTypeVersionAssociation Dependencies { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryTypeVersionAssociation Dependents { get => throw new global::System.NotSupportedException(); }
        public global::System.Guid Guid { get => throw new global::System.NotSupportedException(); }
        public bool IsDefault { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryTypeVersionState State { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryType TypeObject { get => throw new global::System.NotSupportedException(); }
        public global::System.Version VersionNumber { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeVersionAssociation : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.Types.LibraryTypeVersion>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.Types.LibraryTypeVersion> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeVersionComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.Types.LibraryTypeVersion>
    {
        public global::Siemens.Engineering.Library.VersionCreateTransferResults CreateFromDocuments(global::System.IO.DirectoryInfo directoryInfo, string fileNameWithoutExtension, CreateOptions createOptions, global::Siemens.Engineering.Library.LibraryImportOptions libraryImportOptions) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.Types.LibraryTypeVersion> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public enum LibraryTypeVersionState : int
    {
        InWork = 0,
        Committed = 1,
    }
}
