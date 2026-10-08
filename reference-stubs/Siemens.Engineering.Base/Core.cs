// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering
{
    public abstract class ConfirmationEventArgs : global::System.EventArgs, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.ConfirmationResult Result { set => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum ConfirmationResult : int
    {
        Yes = 1,
        No = 6,
    }
    public enum DocumentInfoOptions : int
    {
        None = 0,
    }
    public enum EngineeringAttributeAccessMode : int
    {
        None = 0,
        Read = 1,
        Write = 2,
        ReadWrite = 3,
    }
    public abstract class EngineeringAttributeInfo
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.EngineeringAttributeAccessMode AccessMode { get => throw new global::System.NotSupportedException(); }
        public global::System.Collections.Generic.IEnumerable<global::System.Type> SupportedTypes { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class EngineeringException : global::System.Exception
    {
    }
    public abstract class EngineeringTargetInvocationException : global::Siemens.Engineering.EngineeringException
    {
    }
    public abstract class EngineeringNotSupportedException : global::Siemens.Engineering.EngineeringException
    {
    }
    public abstract class EngineeringObjectDisposedException : global::Siemens.Engineering.EngineeringException
    {
    }
    public abstract class EngineeringSecurityException : global::Siemens.Engineering.EngineeringException
    {
    }
    public abstract class ExclusiveAccess : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::System.IDisposable
    {
        public global::Siemens.Engineering.Transaction Transaction(global::Siemens.Engineering.ITransactionSupport peristence, string undoDescription) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        public void Dispose() => throw new global::System.NotSupportedException();
    }
    public enum ExportOptions : int
    {
        None = 0,
    }
    public abstract class HistoryEntry : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::System.DateTime DateTime { get => throw new global::System.NotSupportedException(); }
        public string Text { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HistoryEntryComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.HistoryEntry>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.HistoryEntry> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public interface IEngineeringComposition : global::Siemens.Engineering.IEngineeringInstance
    {
        global::Siemens.Engineering.IEngineeringObject Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters);
    }
    public interface IEngineeringInstance
    {
        global::Siemens.Engineering.IEngineeringObject Parent { get; }
    }
    public interface IEngineeringObject : global::Siemens.Engineering.IEngineeringInstance
    {
        object GetAttribute(string name);
        void SetAttribute(string name, object value);
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> GetAttributeInfos();
    }
    public interface IEngineeringService : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
    }
    public interface IEngineeringServiceProvider
    {
        T GetService<T>() where T : global::Siemens.Engineering.IEngineeringService;
    }
    public enum ImportOptions : int
    {
        None = 0,
        Override = 1,
    }
    public interface ITransactionSupport
    {
    }
    public abstract class Language : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::System.Globalization.CultureInfo Culture { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LanguageAssociation : global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Language>
    {
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Language> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class LanguageComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Language>
    {
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Language> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class LanguageSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.LanguageComposition Languages { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.LanguageAssociation ActiveLanguages { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Language EditingLanguage { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Language ReferenceLanguage { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class MultilingualText : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.MultilingualTextItemComposition Items { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class MultilingualTextItem : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.Language Language { get => throw new global::System.NotSupportedException(); }
        public string Text { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class MultilingualTextItemComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.MultilingualTextItem>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.MultilingualTextItem> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class NonRecoverableException : global::System.Exception
    {
    }
    public abstract class NotificationEventArgs : global::System.EventArgs, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string Text { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class Project : global::Siemens.Engineering.ProjectBase, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public void Save() => throw new global::System.NotSupportedException();
        public void SaveAs(global::System.IO.DirectoryInfo targetFolderPath) => throw new global::System.NotSupportedException();
        public void Archive(global::System.IO.DirectoryInfo targetDirectory, string targetName, global::Siemens.Engineering.ProjectArchivationMode archivationMode) => throw new global::System.NotSupportedException();
        public void Close() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum ProjectArchivationMode : int
    {
    }
    public abstract class ProjectBase : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.ITransactionSupport, global::Siemens.Engineering.IEngineeringServiceProvider
    {
        public global::Siemens.Engineering.MultilingualText Comment { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HW.DeviceUserGroupComposition DeviceGroups { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HW.DeviceComposition Devices { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HistoryEntryComposition HistoryEntries { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.LanguageSettings LanguageSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HW.SubnetComposition Subnets { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.UsedProductComposition UsedProducts { get => throw new global::System.NotSupportedException(); }
        public string Author { get => throw new global::System.NotSupportedException(); }
        public string Copyright { get => throw new global::System.NotSupportedException(); }
        public global::System.DateTime CreationTime { get => throw new global::System.NotSupportedException(); }
        public string Family { get => throw new global::System.NotSupportedException(); }
        public bool IsModified { get => throw new global::System.NotSupportedException(); }
        public global::System.DateTime LastModified { get => throw new global::System.NotSupportedException(); }
        public string LastModifiedBy { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public global::System.IO.FileInfo Path { get => throw new global::System.NotSupportedException(); }
        public long Size { get => throw new global::System.NotSupportedException(); }
        public string Version { get => throw new global::System.NotSupportedException(); }
        public T GetService<T>() where T : global::Siemens.Engineering.IEngineeringService => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        T global::Siemens.Engineering.IEngineeringServiceProvider.GetService<T>() => throw new global::System.NotSupportedException();
    }
    public abstract class ProjectComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Project>
    {
        public global::Siemens.Engineering.Project Create(global::System.IO.DirectoryInfo targetDirectory, string name) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.Project Open(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Project> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract partial class TiaPortal : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringServiceProvider, global::System.IDisposable
    {
        public global::Siemens.Engineering.HW.HardwareCatalog.HardwareCatalog HardwareCatalog { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.ProjectComposition Projects { get => throw new global::System.NotSupportedException(); }
        public event global::System.EventHandler<global::System.EventArgs> Disposed { add => throw new global::System.NotSupportedException(); remove => throw new global::System.NotSupportedException(); }
        public event global::System.EventHandler<global::Siemens.Engineering.ConfirmationEventArgs> Confirmation { add => throw new global::System.NotSupportedException(); remove => throw new global::System.NotSupportedException(); }
        public event global::System.EventHandler<global::Siemens.Engineering.NotificationEventArgs> Notification { add => throw new global::System.NotSupportedException(); remove => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.ExclusiveAccess ExclusiveAccess() => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.ExclusiveAccess ExclusiveAccess(string text) => throw new global::System.NotSupportedException();
        public static global::System.Collections.Generic.IList<global::Siemens.Engineering.TiaPortalProcess> GetProcesses() => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.TiaPortalProcess GetCurrentProcess() => throw new global::System.NotSupportedException();
        public void Dispose() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        T global::Siemens.Engineering.IEngineeringServiceProvider.GetService<T>() => throw new global::System.NotSupportedException();
    }
    public enum TiaPortalMode : int
    {
        WithUserInterface = 1,
    }
    public abstract class TiaPortalProcess
    {
        public int Id { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.TiaPortalMode Mode { get => throw new global::System.NotSupportedException(); }
        public global::System.IO.FileInfo ProjectPath { get => throw new global::System.NotSupportedException(); }
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.TiaPortalSession> AttachedSessions { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.TiaPortal Attach() => throw new global::System.NotSupportedException();
    }
    public abstract class TiaPortalSession
    {
        public int ProcessId { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class Transaction : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::System.IDisposable
    {
        public void CommitOnDispose() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        public void Dispose() => throw new global::System.NotSupportedException();
    }
    public abstract class UsedProduct : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
        public string Version { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class UsedProductComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.UsedProduct>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.UsedProduct> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
