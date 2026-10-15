// Offline V21 boundary doubles. Recording hooks execute no Siemens implementation code.
using System.Collections;

namespace Siemens.Engineering
{
    public interface IEngineeringService : IEngineeringObject { }
    public interface ITransactionSupport { }

    public sealed partial class TiaPortal
    {
        public Exception? VciExclusiveAccessDisposeFailure { get; set; }
    }

    public sealed class ExclusiveAccess : IDisposable
    {
        private readonly TiaPortal portal;
        public ExclusiveAccess(TiaPortal portal) => this.portal = portal;
        public bool Disposed { get; private set; }
        public Transaction Transaction(ITransactionSupport persistence, string description)
        {
            var transaction = new Transaction((Project)persistence, description) { CanCommit = portal.TransactionCanCommit };
            portal.Transactions.Add(transaction);
            return transaction;
        }
        public void Dispose()
        {
            Disposed = true;
            if (portal.VciExclusiveAccessDisposeFailure is not null) throw portal.VciExclusiveAccessDisposeFailure;
        }
    }

    // shortcut: records commit intent only; model rollback when a test needs engineering-state restoration.
    public sealed class Transaction : IDisposable
    {
        public Transaction(Project project, string description) { Project = project; Description = description; }
        public Project Project { get; }
        public string Description { get; }
        public bool CanCommit { get; set; } = true;
        public bool CommitRequested { get; private set; }
        public bool Disposed { get; private set; }
        public bool Committed => Disposed && CommitRequested && CanCommit;
        public Exception? DisposeFailure { get; set; }
        public void CommitOnDispose()
        {
            if (!CanCommit) throw new EngineeringException("Transaction cannot commit.");
            CommitRequested = true;
        }
        public void Dispose()
        {
            if (DisposeFailure is not null) throw DisposeFailure;
            Disposed = true;
        }
    }
}

namespace Siemens.Engineering.SW
{
    public sealed partial class PlcSoftware
    {
        public TechnologicalObjects.TechnologicalInstanceDBGroup TechnologicalObjectGroup { get; } = new();
    }
}

namespace Siemens.Engineering.SW.TechnologicalObjects
{
    public sealed class TechnologicalInstanceDB : Blocks.InstanceDB { }
    public class TechnologicalInstanceDBGroup : NamedObject
    {
        public TechnologicalInstanceDBGroup() { Groups.Parent = this; TechnologicalObjects.Parent = this; }
        public TechnologicalInstanceDBUserGroupComposition Groups { get; } = new();
        public TechnologicalInstanceDBComposition TechnologicalObjects { get; } = new();
    }
    public sealed class TechnologicalInstanceDBSystemGroup : TechnologicalInstanceDBGroup { }
    public sealed class TechnologicalInstanceDBUserGroup : TechnologicalInstanceDBGroup { }
    public sealed class TechnologicalInstanceDBComposition : Composition<TechnologicalInstanceDB> { }
    public sealed class TechnologicalInstanceDBUserGroupComposition : Composition<TechnologicalInstanceDBUserGroup> { }
}

namespace Siemens.Engineering.VersionControl
{
    public enum CompareState { Equal = 0, Unequal = 1, WorkspaceFileMissing = 2, Unknown = 3 }
    [Flags]
    public enum IndividualObjectCompareDetails { None = 0, ProjectObjectChanged = 1, WorkspaceFileChanged = 2 }
    public enum SynchronizationMode { ProjectToWorkspace = 0, WorkspaceToProject = 1 }

    public sealed class IndividualObjectCompareResult : NamedObject
    {
        public CompareState CompareState { get; set; }
        public IndividualObjectCompareDetails IndividualObjectCompareDetails { get; set; }
    }

    public sealed class VersionControlInterface : NamedObject, IEngineeringService
    {
        public VersionControlInterface() => WorkspaceGroup.Parent = this;
        public WorkspaceSystemGroup WorkspaceGroup { get; } = new();
    }
    public class WorkspaceGroup : NamedObject
    {
        public WorkspaceGroup() => Workspaces.Parent = this;
        public WorkspaceComposition Workspaces { get; } = new();
    }
    public sealed class WorkspaceSystemGroup : WorkspaceGroup { }

    public sealed class WorkspaceComposition : Composition<Workspace>
    {
        public List<(string Name, DirectoryInfo RootPath)> CreateCalls { get; } = new();
        public Exception? CreateFailure { get; set; }
        public Workspace Create(string name, DirectoryInfo rootPath)
        {
            CreateCalls.Add((name, rootPath));
            if (CreateFailure is not null) throw CreateFailure;
            if (Find(name) is not null) throw new EngineeringException("Workspace already exists.");
            var workspace = new Workspace { Name = name, RootPath = rootPath, Owner = this, Parent = Parent };
            Items.Add(workspace);
            return workspace;
        }
    }

    public sealed class Workspace : NamedObject
    {
        public DirectoryInfo RootPath { get; set; } = new(Path.GetTempPath());
        public MappedObjectComposition MappedObjects { get; } = new();
        public WorkspaceComposition? Owner { get; set; }
        public List<(IEngineeringObject Object, DirectoryInfo Directory, string FileName, string Format)> ExportCalls { get; } = new();
        public List<IEngineeringObject> SupportedFormatCalls { get; } = new();
        public Dictionary<IEngineeringObject, IReadOnlyList<string>> SupportedFormats { get; } = new();
        public Dictionary<IEngineeringObject, Exception> ThrowOnLeaf { get; } = new();
        public HashSet<IEngineeringObject> NonRecoverableOnLeaf { get; } = new();
        public Action<IEngineeringObject>? OnExport { get; set; }
        public int DeleteCalls { get; private set; }
        public Exception? DeleteFailure { get; set; }

        internal void CheckLeaf(IEngineeringObject leaf)
        {
            if (NonRecoverableOnLeaf.Contains(leaf)) throw new NonRecoverableException("Synthetic Portal loss.");
            if (ThrowOnLeaf.TryGetValue(leaf, out var failure)) throw failure;
        }
        public IEnumerable<string> GetSupportedFileFormats(IEngineeringObject engineeringObject)
        {
            SupportedFormatCalls.Add(engineeringObject);
            CheckLeaf(engineeringObject);
            return SupportedFormats.TryGetValue(engineeringObject, out var formats) ? formats : new[] { "SimaticML" };
        }
        public MappedObject ExportObject(IEngineeringObject engineeringObject, DirectoryInfo relativeWorkspaceDirectoryPath,
            string fileNameWithoutExtension, string fileFormat)
        {
            ExportCalls.Add((engineeringObject, relativeWorkspaceDirectoryPath, fileNameWithoutExtension, fileFormat));
            CheckLeaf(engineeringObject);
            OnExport?.Invoke(engineeringObject);
            var mapping = new MappedObject
            {
                DirectoryPath = new DirectoryInfo(Path.Combine(RootPath.FullName,
                    relativeWorkspaceDirectoryPath.ToString().TrimStart('\\', '/'))),
                FileNameWithoutExtension = fileNameWithoutExtension,
                FileFormat = fileFormat,
                EngineeringObject = engineeringObject,
                Parent = this,
                Workspace = this,
                Owner = MappedObjects
            };
            MappedObjects.Items.Add(mapping);
            return mapping;
        }
        public void Delete()
        {
            DeleteCalls++;
            if (DeleteFailure is not null) throw DeleteFailure;
            Owner?.Items.Remove(this);
        }
    }

    public sealed class MappedObjectComposition : IEnumerable<MappedObject>
    {
        public List<MappedObject> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public int Count => Items.Count;
        public MappedObject? Find(IEngineeringObject engineeringObject) => this.FirstOrDefault(x => object.Equals(x.EngineeringObject, engineeringObject));
        public IEnumerator<MappedObject> GetEnumerator()
        {
            if (EnumerationFailure is not null) throw EnumerationFailure;
            return Items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class MappedObject : NamedObject
    {
        public DirectoryInfo DirectoryPath { get; set; } = new(Path.GetTempPath());
        public string FileNameWithoutExtension { get; set; } = "";
        public string FileFormat { get; set; } = "SimaticML";
        public IEngineeringObject? EngineeringObject { get; set; }
        public Workspace? Workspace { get; set; }
        public MappedObjectComposition? Owner { get; set; }
        public IndividualObjectCompareResult StatusResult { get; set; } = new();
        public Exception? StatusFailure { get; set; }
        public Exception? SynchronizeFailure { get; set; }
        public Exception? DeleteFailure { get; set; }
        public Action<SynchronizationMode>? OnSynchronize { get; set; }
        public Action? OnGetStatus { get; set; }
        public int GetStatusCalls { get; private set; }
        public List<SynchronizationMode> SynchronizeCalls { get; } = new();
        public int DeleteCalls { get; private set; }
        public IndividualObjectCompareResult GetStatus()
        {
            GetStatusCalls++;
            OnGetStatus?.Invoke();
            if (EngineeringObject is not null) Workspace?.CheckLeaf(EngineeringObject);
            if (StatusFailure is not null) throw StatusFailure;
            return StatusResult;
        }
        public void Synchronize(SynchronizationMode mode)
        {
            SynchronizeCalls.Add(mode);
            if (EngineeringObject is not null) Workspace?.CheckLeaf(EngineeringObject);
            if (SynchronizeFailure is not null) throw SynchronizeFailure;
            OnSynchronize?.Invoke(mode);
            StatusResult = new IndividualObjectCompareResult { CompareState = CompareState.Equal };
        }
        public void Delete()
        {
            DeleteCalls++;
            if (DeleteFailure is not null) throw DeleteFailure;
            Owner?.Items.Remove(this);
        }
    }
}
