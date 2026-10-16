// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.VersionControl
{
    public abstract class VersionControlInterface : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        public global::Siemens.Engineering.VersionControl.WorkspaceSystemGroup WorkspaceGroup { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class WorkspaceGroup : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.VersionControl.WorkspaceComposition Workspaces { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class WorkspaceSystemGroup : global::Siemens.Engineering.VersionControl.WorkspaceGroup
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class WorkspaceComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.VersionControl.Workspace>
    {
        public int Count { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.VersionControl.Workspace Create(string name, global::System.IO.DirectoryInfo rootPath) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.VersionControl.Workspace Find(string name) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.VersionControl.Workspace> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class Workspace : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
        public global::System.IO.DirectoryInfo RootPath { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.VersionControl.MappedObjectComposition MappedObjects { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.VersionControl.MappedObject ExportObject(global::Siemens.Engineering.IEngineeringObject engineeringObject, global::System.IO.DirectoryInfo relativeWorkspaceDirectoryPath, string fileNameWithoutExtension, string fileFormat) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerable<string> GetSupportedFileFormats(global::Siemens.Engineering.IEngineeringObject engineeringObject) => throw new global::System.NotSupportedException();
        public void Delete() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class MappedObjectComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.VersionControl.MappedObject>
    {
        public int Count { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.VersionControl.MappedObject Find(global::Siemens.Engineering.IEngineeringObject engineeringObject) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.VersionControl.MappedObject> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class MappedObject : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::System.IO.DirectoryInfo DirectoryPath { get => throw new global::System.NotSupportedException(); }
        public string FileNameWithoutExtension { get => throw new global::System.NotSupportedException(); }
        public string FileFormat { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.IEngineeringObject EngineeringObject { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.VersionControl.IndividualObjectCompareResult GetStatus() => throw new global::System.NotSupportedException();
        public void Synchronize(global::Siemens.Engineering.VersionControl.SynchronizationMode mode) => throw new global::System.NotSupportedException();
        public void Delete() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class IndividualObjectCompareResult : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.VersionControl.CompareState CompareState { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.VersionControl.IndividualObjectCompareDetails IndividualObjectCompareDetails { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum CompareState : int
    {
        Equal = 0,
        Unequal = 1,
        WorkspaceFileMissing = 2,
        Unknown = 3,
    }
    [global::System.Flags]
    public enum IndividualObjectCompareDetails : int
    {
        None = 0,
        ProjectObjectChanged = 1,
        WorkspaceFileChanged = 2,
    }
    public enum SynchronizationMode : int
    {
        ProjectToWorkspace = 0,
        WorkspaceToProject = 1,
    }
}
