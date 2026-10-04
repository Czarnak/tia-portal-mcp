// Executable boundary doubles only. Tests never load the compile-only Siemens references.
namespace Siemens.Engineering
{
    public abstract class ProjectBase
    {
        private FileInfo? path;
        private bool isModified;
        public Exception? PathFailure { get; set; }
        public Exception? ModifiedFailure { get; set; }
        public List<string> Calls { get; } = new();
        public FileInfo? Path
        {
            get => PathFailure is null ? path : throw PathFailure;
            set => path = value;
        }
        public bool IsModified
        {
            get { Calls.Add("IsModified"); return ModifiedFailure is null ? isModified : throw ModifiedFailure; }
            set => isModified = value;
        }
    }
}

namespace Siemens.Engineering.Multiuser
{
    public sealed class MultiuserProject : ProjectBase { }
    public sealed class LocalSession
    {
        public MultiuserProject Project { get; set; } = new();
        public int SaveCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public void Save() => SaveCalls++;
        public void Close() => CloseCalls++;
        public void CloseAndCommit() => CommitCalls++;
    }
}
