// Compile-only V21 declarations (library read subset). No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.Library
{
    public interface ILibrary
    {
        global::Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder MasterCopyFolder { get; }
        global::Siemens.Engineering.Library.Types.LibraryTypeSystemFolder TypeFolder { get; }
    }
    public abstract class GlobalLibrary : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.ITransactionSupport, global::Siemens.Engineering.Library.ILibrary
    {
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
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.Library.GlobalLibraryInfo> GetGlobalLibraryInfos() => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.GlobalLibrary> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}

namespace Siemens.Engineering.Library.MasterCopies
{
    public abstract class MasterCopy : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
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
    }
    public abstract class MasterCopyUserFolderComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.MasterCopies.MasterCopyUserFolder>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.MasterCopies.MasterCopyUserFolder> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class MasterCopyComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.MasterCopies.MasterCopy>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.MasterCopies.MasterCopy> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}

namespace Siemens.Engineering.Library.Types
{
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
    }
    public abstract class LibraryTypeUserFolderComposition : global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.Types.LibraryTypeUserFolder>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.Types.LibraryTypeUserFolder> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeVersion : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
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
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.Types.LibraryTypeVersion> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public enum LibraryTypeVersionState : int
    {
        InWork = 0,
        Committed = 1,
    }
}
