using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;

// Native library-write rejection probe. Every case calls Siemens Openness directly with no
// worker pre-checks, on fresh per-case copies of a disposable project and global library.
internal static class Program
{
    internal const string ApiPath = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
    internal const int TimeoutSeconds = 900;
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 20000000, RecursionLimit = 150 };
    private static readonly object OutputGate = new object();
    private static StreamWriter output;
    private static Timer watchdog;
    internal static string CurrentPhase { get; private set; } = "initializing";
    internal static string WorkRoot;

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var name = new AssemblyName(args.Name);
            if (!name.Name.StartsWith("Siemens.Engineering.", StringComparison.Ordinal)) return null;
            var path = Path.Combine(ApiPath, name.Name + ".dll");
            if (!File.Exists(path)) return null;
            var loaded = Assembly.LoadFrom(path);
            if (name.FullName != loaded.GetName().FullName) throw new FileLoadException("Exact installed Openness assembly identity mismatch.", path);
            return loaded;
        };
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (args.Length == 1 && args[0] == "--list") { Emit(new { cases = Catalog.All.Select(c => new { id = c.Id, question = c.Question }) }); return 0; }
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest.Run();
            var options = Options.Parse(args);
            WorkRoot = options.Work;
            Directory.CreateDirectory(options.Work);
            output = new StreamWriter(options.OutputPath, true, new UTF8Encoding(false)) { AutoFlush = true };
            try
            {
                Emit(new { type = "startup", processId = Process.GetCurrentProcess().Id, is64BitProcess = Environment.Is64BitProcess,
                    apartment = Thread.CurrentThread.GetApartmentState().ToString(), runtime = Environment.Version.ToString(),
                    timeoutSeconds = TimeoutSeconds, mode = options.CheckFixture ? "check-fixture" : "cases", cases = options.Cases });
                return Execute(options);
            }
            catch (Exception ex) { Emit(new { type = "fatal", phase = CurrentPhase, error = Error(ex) }); return 1; }
            finally { watchdog?.Dispose(); output.Dispose(); output = null; }
        }
        catch (Exception ex) { Emit(new { type = "fatal", phase = CurrentPhase, error = Error(ex) }); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Execute(Options options) { return new Runner(options).Run(); }

    internal static void Emit(object value)
    {
        lock (OutputGate)
        {
            var line = Json.Serialize(value);
            output?.WriteLine(line);
            Console.WriteLine(line);
            Console.Out.Flush();
        }
    }

    internal static void StartRequest(string phase)
    {
        Progress(phase);
        if (watchdog == null) watchdog = new Timer(_ =>
        {
            Emit(new { type = "timeout", timeoutSeconds = TimeoutSeconds, phase = CurrentPhase, utc = DateTime.UtcNow.ToString("O"),
                message = "Probe request exceeded the watchdog; exiting only the probe process. No dialog accepted, no retry, Portal not killed." });
            Environment.Exit(124);
        }, null, TimeoutSeconds * 1000, Timeout.Infinite);
        else watchdog.Change(TimeoutSeconds * 1000, Timeout.Infinite);
    }

    internal static void Progress(string phase)
    {
        CurrentPhase = phase;
        Emit(new { type = "progress", phase, utc = DateTime.UtcNow.ToString("O") });
    }

    internal static object Error(Exception ex)
    {
        var chain = new List<object>();
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            chain.Add(new { type = inner.GetType().FullName, message = inner.Message, stackTrace = inner.StackTrace });
        return new { type = ex.GetType().FullName, message = ex.Message, stackTrace = ex.StackTrace, inner = chain };
    }

    internal static bool IsFatal(Exception ex) => ex is NonRecoverableException || (ex.InnerException != null && IsFatal(ex.InnerException));

    internal static bool Under(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    // Paths are reported relative to the work root so traces carry no profile or project paths.
    internal static string Rel(string path)
    {
        if (path == null) return null;
        return WorkRoot != null && Under(path, WorkRoot) ? "work\\" + Path.GetFullPath(path).Substring(WorkRoot.TrimEnd('\\').Length + 1) : "<outside-work>";
    }
}

internal static class Names
{
    internal const string Plc1 = "PLC_1", Plc2 = "PLC_2", Lib = "LP_ProbeLib";
    internal const string Udt = "LP_Udt", Fb = "LP_Fb", Free = "LP_Free", PlainUdt = "LP_PlainUdt";
    internal const string GUdt = "LP_GUdt", GFb = "LP_GFb", TypeFolder = "LP_TypeFolder", McFolder = "LP_McFolder";
}

internal sealed class Options
{
    internal int Pid;
    internal string Root, ProjectFile, LibraryFile, Work, OutputPath;
    internal string[] Cases = new string[0];
    internal bool CheckFixture;
    internal string ProjectDir => Path.GetDirectoryName(ProjectFile);
    internal string LibraryDir => Path.GetDirectoryName(LibraryFile);

    internal static Options Parse(string[] args)
    {
        var o = new Options();
        bool all = false, confirm = false;
        string selected = null, output = null;
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + args[i]);
            switch (args[i])
            {
                case "--pid": if (!int.TryParse(Next(), out o.Pid)) throw new ArgumentException("--pid must be an integer."); break;
                case "--disposable-root": o.Root = Path.GetFullPath(Next()); break;
                case "--project": o.ProjectFile = Path.GetFullPath(Next()); break;
                case "--library": o.LibraryFile = Path.GetFullPath(Next()); break;
                case "--work": o.Work = Path.GetFullPath(Next()); break;
                case "--output": output = Path.GetFullPath(Next()); break;
                case "--case": selected = Next(); break;
                case "--all": all = true; break;
                case "--check-fixture": o.CheckFixture = true; break;
                case "--confirm-disposable": confirm = true; break;
                default: throw new ArgumentException("Unknown argument: " + args[i]);
            }
        }
        if (!confirm) throw new ArgumentException("--confirm-disposable is required. No attachment was attempted.");
        if (o.Pid <= 0) throw new ArgumentException("--pid is required.");
        if (o.Root == null || !Directory.Exists(o.Root)) throw new ArgumentException("--disposable-root must be an existing directory.");
        if (o.ProjectFile == null || !File.Exists(o.ProjectFile) || !o.ProjectFile.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase) || !Program.Under(o.ProjectFile, o.Root))
            throw new ArgumentException("--project must be an existing .ap21 inside --disposable-root.");
        if (o.LibraryFile == null || !File.Exists(o.LibraryFile) || !o.LibraryFile.EndsWith(".al21", StringComparison.OrdinalIgnoreCase) || !Program.Under(o.LibraryFile, o.Root))
            throw new ArgumentException("--library must be an existing .al21 inside --disposable-root.");
        if (Path.GetFileNameWithoutExtension(o.LibraryFile) != Names.Lib || Path.GetFileName(o.LibraryDir) != Names.Lib)
            throw new ArgumentException("--library must be " + Names.Lib + "\\" + Names.Lib + ".al21.");
        if (string.Equals(o.ProjectDir, o.Root, StringComparison.OrdinalIgnoreCase) || string.Equals(o.LibraryDir, o.Root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(o.ProjectDir, o.LibraryDir, StringComparison.OrdinalIgnoreCase)
            || Program.Under(o.ProjectDir, o.LibraryDir) || Program.Under(o.LibraryDir, o.ProjectDir))
            throw new ArgumentException("Project and library must live in separate folders below --disposable-root.");
        if (o.Work == null || !Program.Under(o.Work, o.Root) || Directory.Exists(o.Work) || File.Exists(o.Work)
            || Program.Under(o.Work, o.ProjectDir) || Program.Under(o.Work, o.LibraryDir))
            throw new ArgumentException("--work must be a new path inside --disposable-root, outside the project and library folders.");
        if ((all ? 1 : 0) + (selected != null ? 1 : 0) + (o.CheckFixture ? 1 : 0) != 1)
            throw new ArgumentException("Specify exactly one of --all, --case IDS or --check-fixture.");
        if (all) o.Cases = Catalog.All.Select(c => c.Id).ToArray();
        if (selected != null)
        {
            o.Cases = selected.Split(',').Select(s => s.Trim()).ToArray();
            var unknown = o.Cases.Where(id => Catalog.All.All(c => c.Id != id)).ToArray();
            if (unknown.Length != 0 || o.Cases.Length == 0) throw new ArgumentException("Unknown case: " + string.Join(",", unknown));
        }
        o.OutputPath = output ?? Path.Combine(o.Work, "results.jsonl");
        if (!Program.Under(o.OutputPath, o.Work)) throw new ArgumentException("--output must stay inside --work.");
        return o;
    }
}

internal enum LibMode { None, ReadWrite, ReadOnly }

internal sealed class ProbeSetupException : Exception
{
    internal ProbeSetupException(string message) : base(message) { }
}

internal sealed class Case
{
    internal string Id, Question;
    internal bool Project;
    internal LibMode Library;
    internal Action<Ctx> Setup, Act, Inspect;
}

internal sealed class Ctx
{
    internal TiaPortal Portal;
    internal Project Project;
    internal UserGlobalLibrary Lib;
    internal PlcSoftware Plc1, Plc2;
    internal string CaseDir, OutDir, LibDir, LibFile;
    internal string Stage = "prepare";
    internal readonly Dictionary<string, object> Notes = new Dictionary<string, object>();
    internal ProjectLibrary PL => Project.ProjectLibrary;
    internal void At(string stage) { Stage = stage; Program.Progress(CaseDir == null ? stage : Path.GetFileName(CaseDir) + "." + stage); }
    internal void Note(string key, object value) { Notes[key] = Describe.Value(value); }
    internal string Out(string relative) => Path.Combine(OutDir, relative);

    internal LibraryTypeVersion EditUdt()
    {
        At("setup:LibraryTypeVersion.Edit(LibraryTypeInstanceInfo)");
        var info = F.PlcTypeOf(Plc1, Names.Udt).GetService<LibraryTypeInstanceInfo>() ?? throw new ProbeSetupException("PLC_1 LP_Udt is not a type instance.");
        var inWork = info.LibraryTypeVersion.Edit(info);
        Note("setupInWork", new { guid = inWork.Guid.ToString(), number = inWork.VersionNumber.ToString(), state = inWork.State.ToString() });
        return inWork;
    }

    internal void ReleaseUdt110()
    {
        var inWork = EditUdt();
        At("setup:LibraryTypeVersion.Release(1.1.0)");
        inWork.Release(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, new Version(1, 1, 0), "probe", "setup");
    }

    internal void ArchiveBase()
    {
        At("setup:UserGlobalLibrary.Archive(base.zal21)");
        Lib.Archive(new DirectoryInfo(OutDir), "base.zal21", LibraryArchivationMode.Compressed);
    }
}

// Native finders. A missing fixture object is a setup error, not a probe observation.
internal static class F
{
    internal static IEnumerable<LibraryType> Types(LibraryTypeFolder folder) =>
        folder.Types.Concat(folder.Folders.SelectMany(Types));

    internal static LibraryType Type(ILibrary library, string name)
    {
        var matches = Types(library.TypeFolder).Where(t => t.Name == name).ToList();
        if (matches.Count != 1) throw new ProbeSetupException("Expected exactly one library type " + name + ", found " + matches.Count + ".");
        return matches[0];
    }

    internal static LibraryTypeVersion Version(LibraryType type, string number) =>
        type.Versions.SingleOrDefault(v => v.VersionNumber.ToString() == number) ?? throw new ProbeSetupException("Missing version " + type.Name + " " + number + ".");

    internal static LibraryTypeVersion Default(LibraryType type) =>
        type.Versions.SingleOrDefault(v => v.IsDefault) ?? throw new ProbeSetupException("No single default version on " + type.Name + ".");

    internal static MasterCopy Mc(MasterCopyFolder folder, string name) =>
        folder.MasterCopies.Find(name) ?? throw new ProbeSetupException("Missing master copy " + name + ".");

    internal static PlcBlock Block(PlcSoftware plc, string name) => FindBlock(plc.BlockGroup, name) ?? throw new ProbeSetupException("Missing block " + name + " on " + plc.Name + ".");
    private static PlcBlock FindBlock(PlcBlockGroup group, string name) =>
        group.Blocks.Find(name) ?? group.Groups.Select(g => FindBlock(g, name)).FirstOrDefault(b => b != null);

    internal static PlcType PlcTypeOf(PlcSoftware plc, string name) => FindType(plc.TypeGroup, name) ?? throw new ProbeSetupException("Missing PLC type " + name + " on " + plc.Name + ".");
    private static PlcType FindType(PlcTypeGroup group, string name) =>
        group.Types.Find(name) ?? group.Groups.Select(g => FindType(g, name)).FirstOrDefault(t => t != null);

    internal static IEnumerable<string> BlockNames(PlcBlockGroup g) => g.Blocks.Select(b => b.Name).Concat(g.Groups.SelectMany(BlockNames));
    internal static IEnumerable<string> TypeNames(PlcTypeGroup g) => g.Types.Select(t => t.Name).Concat(g.Groups.SelectMany(TypeNames));

    internal static IMasterCopySource Source(object o) => o as IMasterCopySource ?? throw new ProbeSetupException(o.GetType().Name + " is not an IMasterCopySource.");

    internal static T As<T>(LibraryTypeVersion v) where T : class => v as T ?? throw new ProbeSetupException("Version is " + v.GetType().FullName + ", not " + typeof(T).Name + ".");

    internal static void ExportXml(Ctx c, PlcType type, string name)
    {
        c.At("setup:PlcType.Export(" + name + ".xml)");
        Directory.CreateDirectory(c.Out("docs"));
        type.Export(new FileInfo(Path.Combine(c.Out("docs"), name + ".xml")), ExportOptions.None);
    }
}

internal static class Catalog
{
    private static Case C(string id, string question, bool project, LibMode library, Action<Ctx> setup, Action<Ctx> act, Action<Ctx> inspect = null) =>
        new Case { Id = id, Question = question, Project = project, Library = library, Setup = setup, Act = act, Inspect = inspect };

    private const LibMode None = LibMode.None, RW = LibMode.ReadWrite, RO = LibMode.ReadOnly;
    private const DeleteUnusedVersionsMode Keep = DeleteUnusedVersionsMode.DoNotDelete;
    private const StructureConflictResolutionMode Cancel = StructureConflictResolutionMode.CancelIfStructureConflicts;
    private const UpdatePathsMode Throw = UpdatePathsMode.ThrowIfPathsConflict;

    internal static readonly Case[] All =
    {
        // Global library lifecycle
        C("G1", "Create a global library whose destination folder already exists.", false, None, null,
            c => c.Note("returned", c.Portal.GlobalLibraries.Create<UserGlobalLibrary>(new DirectoryInfo(Path.GetDirectoryName(c.LibDir)), Names.Lib))),
        C("G2", "Create a global library with an invalid file-system name.", false, None, null,
            c => c.Note("returned", c.Portal.GlobalLibraries.Create<UserGlobalLibrary>(new DirectoryInfo(c.OutDir), "LP:Bad"))),
        C("G3", "Open the same library file again in the same mode (read-write).", false, RW, null,
            c => { var again = c.Portal.GlobalLibraries.Open(new FileInfo(c.LibFile), OpenMode.ReadWrite); c.Note("sameObject", again.Equals(c.Lib)); }),
        C("G4", "Open the same library file again in the other mode (read-only while read-write).", false, RW, null,
            c => { var again = c.Portal.GlobalLibraries.Open(new FileInfo(c.LibFile), OpenMode.ReadOnly); c.Note("sameObject", again.Equals(c.Lib)); c.Note("againIsReadOnly", again.IsReadOnly); }),
        C("G5", "Open a second library with the same name from another path.", false, RW,
            c => { c.At("setup:copy-second-library"); Runner.CopyDir(c.LibDir, Path.Combine(c.CaseDir, "lib2", Names.Lib)); },
            c => c.Note("returned", c.Portal.GlobalLibraries.Open(new FileInfo(Path.Combine(c.CaseDir, "lib2", Names.Lib, Names.Lib + ".al21")), OpenMode.ReadWrite))),
        C("G6", "Retrieve an archive into a non-empty directory.", false, RW,
            c => { c.ArchiveBase(); Directory.CreateDirectory(c.Out("occupied")); File.WriteAllText(Path.Combine(c.Out("occupied"), "occupied.txt"), "x"); },
            c => c.Note("returned", c.Portal.GlobalLibraries.Retrieve(new FileInfo(c.Out("base.zal21")), new DirectoryInfo(c.Out("occupied")), OpenMode.ReadOnly))),
        C("G7", "Retrieve an archive into a directory that does not exist.", false, RW, c => c.ArchiveBase(),
            c => c.Note("returned", c.Portal.GlobalLibraries.Retrieve(new FileInfo(c.Out("base.zal21")), new DirectoryInfo(c.Out("missing")), OpenMode.ReadOnly))),
        C("G8", "Save a library opened read-only.", false, RO, null, c => c.Lib.Save()),
        C("G9", "Close a library with unsaved changes (does Close save, discard, prompt or throw?).", false, RW,
            c => { c.At("setup:MasterCopyFolder.Folders.Create(LP_Dirty)"); c.Lib.MasterCopyFolder.Folders.Create("LP_Dirty"); },
            c => c.Lib.Close()),
        C("G10", "Archive a library with unsaved changes (is the unsaved change in the archive?).", false, RW,
            c => { c.At("setup:MasterCopyFolder.Folders.Create(LP_Dirty)"); c.Lib.MasterCopyFolder.Folders.Create("LP_Dirty"); },
            c => c.Lib.Archive(new DirectoryInfo(c.OutDir), "dirty.zal21", LibraryArchivationMode.Compressed),
            c => { c.At("inspect:Retrieve(dirty.zal21)"); Directory.CreateDirectory(c.Out("dirty-retrieved"));
                   c.Note("retrieved", c.Portal.GlobalLibraries.Retrieve(new FileInfo(c.Out("dirty.zal21")), new DirectoryInfo(c.Out("dirty-retrieved")), OpenMode.ReadOnly)); }),
        C("G11", "Archive twice to the same archive file name.", false, RW,
            c => { c.At("setup:UserGlobalLibrary.Archive(dup.zal21)"); c.Lib.Archive(new DirectoryInfo(c.OutDir), "dup.zal21", LibraryArchivationMode.Compressed);
                   c.Note("firstArchive", Describe.File(c.Out("dup.zal21"))); },
            c => c.Lib.Archive(new DirectoryInfo(c.OutDir), "dup.zal21", LibraryArchivationMode.Compressed)),
        C("G12", "Archive into the library's own folder.", false, RW, null,
            c => c.Lib.Archive(new DirectoryInfo(c.LibDir), "self.zal21", LibraryArchivationMode.Compressed)),
        C("G13", "Archive with a name that has no .zal21 extension.", false, RW, null,
            c => c.Lib.Archive(new DirectoryInfo(c.OutDir), "noext", LibraryArchivationMode.Compressed)),

        // Folders
        C("F1", "Create a master-copy folder whose name already exists.", false, RW, null, c => c.Note("returned", c.Lib.MasterCopyFolder.Folders.Create(Names.McFolder))),
        C("F2", "Create a master-copy folder that differs only in case.", false, RW, null, c => c.Note("returned", c.Lib.MasterCopyFolder.Folders.Create(Names.McFolder.ToLowerInvariant()))),
        C("F3", "Create a type folder whose name already exists.", false, RW, null, c => c.Note("returned", c.Lib.TypeFolder.Folders.Create(Names.TypeFolder))),
        C("F4", "Create a folder in a library opened read-only.", false, RO, null, c => c.Note("returned", c.Lib.MasterCopyFolder.Folders.Create("LP_New"))),
        C("F5", "Delete a non-empty master-copy folder.", false, RW, null, c => c.Lib.MasterCopyFolder.Folders.Find(Names.McFolder).Delete()),
        C("F6", "Delete a type folder that contains types.", false, RW, null, c => c.Lib.TypeFolder.Folders.Find(Names.TypeFolder).Delete()),

        // Master copies
        C("M1", "Create a master copy from a PLC block whose name already exists in the target folder.", true, RW, null,
            c => c.Note("returned", c.Lib.MasterCopyFolder.MasterCopies.Create(F.Source(F.Block(c.Plc1, Names.Free))))),
        C("M2", "Copy a master copy between libraries onto an existing name.", true, RW, null,
            c => c.Note("returned", c.Lib.MasterCopyFolder.MasterCopies.CreateFrom(F.Mc(c.PL.MasterCopyFolder, Names.Free)))),
        C("M3", "Copy a master copy into its own folder.", false, RW, null,
            c => c.Note("returned", c.Lib.MasterCopyFolder.MasterCopies.CreateFrom(F.Mc(c.Lib.MasterCopyFolder, Names.Free)))),
        C("M4", "Create a master copy in a library opened read-only (no name collision).", true, RO, null,
            c => c.Note("returned", c.Lib.MasterCopyFolder.MasterCopies.Create(F.Source(F.PlcTypeOf(c.Plc1, Names.PlainUdt))))),
        C("M5", "Instantiate a master copy into a PLC with ThrowIfExists over an existing block.", true, RW, null,
            c => c.Note("returned", c.Plc1.BlockGroup.Blocks.CreateFrom(F.Mc(c.Lib.MasterCopyFolder, Names.Free), MasterCopyMode.ThrowIfExists))),
        C("M6", "Instantiate a master copy with the default overload over an existing block.", true, RW, null,
            c => c.Note("returned", c.Plc1.BlockGroup.Blocks.CreateFrom(F.Mc(c.Lib.MasterCopyFolder, Names.Free)))),
        C("M7", "Instantiate a block master copy into a PLC type composition.", true, RW, null,
            c => c.Note("returned", c.Plc2.TypeGroup.Types.CreateFrom(F.Mc(c.Lib.MasterCopyFolder, Names.Free)))),
        C("M8", "Create a global master copy from a library-type instance block (do type versions follow?).", true, RW, null,
            c => c.Note("returned", c.Lib.MasterCopyFolder.MasterCopies.Create(F.Source(F.Block(c.Plc1, Names.Fb))))),

        // Library types and versions
        C("T1", "Create a project-library type from a document whose name matches an existing type.", true, None,
            c => F.ExportXml(c, F.PlcTypeOf(c.Plc1, Names.Udt), Names.Udt),
            c => c.Note("returned", c.PL.TypeFolder.Types.CreateFromDocuments(new DirectoryInfo(c.Out("docs")), Names.Udt, c.Plc2.TypeGroup, LibraryImportOptions.None))),
        C("T2", "Create a type from a document into a PLC group that already holds that object (no library collision).", true, None,
            c => F.ExportXml(c, F.PlcTypeOf(c.Plc1, Names.PlainUdt), Names.PlainUdt),
            c => c.Note("returned", c.PL.TypeFolder.Types.CreateFromDocuments(new DirectoryInfo(c.Out("docs")), Names.PlainUdt, c.Plc1.TypeGroup, LibraryImportOptions.None))),
        C("T3", "Create a version from a document with CreateOptions.None while an in-work version exists.", true, None,
            c => { F.ExportXml(c, F.PlcTypeOf(c.Plc1, Names.Udt), Names.Udt); c.EditUdt(); },
            c => c.Note("returned", F.Type(c.PL, Names.Udt).Versions.CreateFromDocuments(new DirectoryInfo(c.Out("docs")), Names.Udt, c.Plc2.TypeGroup, CreateOptions.None, LibraryImportOptions.None))),
        C("T4", "Create a version from a document with CreateOptions.Override while an in-work version exists.", true, None,
            c => { F.ExportXml(c, F.PlcTypeOf(c.Plc1, Names.Udt), Names.Udt); c.EditUdt(); },
            c => c.Note("returned", F.Type(c.PL, Names.Udt).Versions.CreateFromDocuments(new DirectoryInfo(c.Out("docs")), Names.Udt, c.Plc2.TypeGroup, CreateOptions.Override, LibraryImportOptions.None))),
        C("T5", "Edit a released version again while an in-work version already exists.", true, None, c => c.EditUdt(),
            c => { var info = F.PlcTypeOf(c.Plc1, Names.Udt).GetService<LibraryTypeInstanceInfo>(); c.Note("returned", F.Version(F.Type(c.PL, Names.Udt), "1.0.0").Edit(info)); }),
        C("T6", "Release a dependent's in-work version with DoNotAutomaticallyCreateOrReleaseDependencies while its dependency is in work.", true, None,
            c => { c.EditUdt(); c.Note("fbVersionsAfterUdtEdit", F.Type(c.PL, Names.Fb).Versions.Select(v => v.VersionNumber + ":" + v.State).ToList()); },
            c =>
            {
                var fbInWork = F.Type(c.PL, Names.Fb).Versions.FirstOrDefault(v => v.State == LibraryTypeVersionState.InWork);
                c.Note("dependentInWorkCreated", fbInWork != null);
                if (fbInWork == null) return; // the cascade observation is the evidence
                c.At("act:Release(LP_Fb in-work)");
                fbInWork.Release(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, new Version(1, 1, 0), "probe", "dependent");
            }),
        C("T7", "Release with a version number lower than the existing release.", true, None, null,
            c => { var w = c.EditUdt(); c.At("act:Release(0.9.0)"); w.Release(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, new Version(0, 9, 0), "probe", "lower"); }),
        C("T8", "Release with the same version number as the existing release.", true, None, null,
            c => { var w = c.EditUdt(); c.At("act:Release(1.0.0)"); w.Release(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, new Version(1, 0, 0), "probe", "same"); }),
        C("T9", "Set an in-work version as default.", true, None, null, c => { var w = c.EditUdt(); c.At("act:SetAsDefault(in-work)"); w.SetAsDefault(); }),
        C("T10", "Delete the default version of a type that has two released versions.", true, None, c => c.ReleaseUdt110(),
            c => F.Default(F.Type(c.PL, Names.Udt)).Delete()),
        C("T11", "Delete the only version of a type with no dependents or instances.", false, RW, null,
            c => F.Version(F.Type(c.Lib, Names.GFb), "1.0.0").Delete()),
        C("T12", "Delete a project-library type that has PLC instances.", true, None, null, c => F.Type(c.PL, Names.Fb).Delete()),
        C("T13", "Delete a project-library version that has PLC instances.", true, None, null, c => F.Version(F.Type(c.PL, Names.Fb), "1.0.0").Delete()),
        C("T14", "Delete a global type that another type depends on.", false, RW, null, c => F.Type(c.Lib, Names.GUdt).Delete()),
        C("T15", "UpdateLibrary with the same library as source and target.", true, None, null, c => F.Type(c.PL, Names.Udt).UpdateLibrary(c.PL)),
        C("T16", "UpdateLibrary global to project with ForceUpdateMode.NoDefaultVersionChange.", true, RW, null,
            c => F.Type(c.Lib, Names.GUdt).UpdateLibrary(c.PL, Keep, Cancel, ForceUpdateMode.NoDefaultVersionChange)),
        C("T17", "UpdateLibrary a type whose dependency is missing in the target (is it pulled in?).", true, RW, null,
            c => F.Type(c.Lib, Names.GFb).UpdateLibrary(c.PL, Keep, Cancel, ForceUpdateMode.SetOnlyHigherUpdatedVersionAsDefault)),
        C("T18", "UpdateLibrary a new version into a global library opened read-only.", true, RO, c => c.ReleaseUdt110(),
            c => F.Type(c.PL, Names.Udt).UpdateLibrary(c.Lib, Keep, Cancel, ForceUpdateMode.SetOnlyHigherUpdatedVersionAsDefault)),
        C("T19", "Instantiate a type version with ThrowIfPathsConflict where its instance already exists.", true, None, null,
            c => c.Note("returned", c.Plc1.TypeGroup.Types.CreateFrom(F.As<PlcTypeLibraryTypeVersion>(F.Version(F.Type(c.PL, Names.Udt), "1.0.0")), Throw))),
        C("T20", "Instantiate an FB type version into a PLC that lacks its UDT dependency.", true, None, null,
            c => c.Note("returned", c.Plc2.BlockGroup.Blocks.CreateFrom(F.As<CodeBlockLibraryTypeVersion>(F.Version(F.Type(c.PL, Names.Fb), "1.0.0")), Throw))),
        C("T21", "Instantiate a global-library type version directly (is it synchronized into the project library?).", true, RW, null,
            c => c.Note("returned", c.Plc2.TypeGroup.Types.CreateFrom(F.As<PlcTypeLibraryTypeVersion>(F.Version(F.Type(c.Lib, Names.GUdt), "1.0.0")), Throw))),
        C("T22", "Instantiate a type version over a same-named plain PLC object.", true, RW, null,
            c => c.Note("returned", c.Plc1.TypeGroup.Types.CreateFrom(F.As<PlcTypeLibraryTypeVersion>(F.Version(F.Type(c.Lib, Names.GUdt), "1.0.0")), Throw))),
        C("T23", "Discard a released (committed) version.", true, None, null, c => F.Version(F.Type(c.PL, Names.Udt), "1.0.0").Discard()),
        C("T24", "Delete a type in a library opened read-only.", false, RO, null, c => F.Type(c.Lib, Names.GFb).Delete()),

        // Project update
        C("U1", "UpdateProject with DoNotDelete/RetainStructure/NoDefaultVersionChange after a new release (what changes on the PLC?).", true, None, c => c.ReleaseUdt110(),
            c => F.Type(c.PL, Names.Udt).UpdateProject(c.Plc1 as IUpdateProjectScope ?? throw new ProbeSetupException("PlcSoftware is not an IUpdateProjectScope."),
                Keep, StructureConflictResolutionMode.RetainStructure, ForceUpdateMode.NoDefaultVersionChange)),
    };
}

// Snapshots and value descriptions. Every native read is isolated so one failing read cannot hide the rest.
internal static class Describe
{
    internal static object Safe(Func<object> read)
    {
        try { return read(); }
        catch (Exception ex) when (!Program.IsFatal(ex)) { return new { readError = ex.GetType().FullName, message = ex.Message }; }
    }

    internal static object Value(object o, int depth = 0)
    {
        if (o == null) return null;
        if (o is string || o is bool || o is Enum || o is Guid || o is Version || o is DateTime || o.GetType().IsPrimitive) return o.ToString();
        if (o is GlobalLibrary g) return new { clrType = o.GetType().FullName, name = Safe(() => g.Name), path = Safe(() => Program.Rel(g.Path.FullName)), isReadOnly = Safe(() => g.IsReadOnly) };
        if (depth >= 3) return o.GetType().FullName;
        if (o is IEnumerable e && !(o is IEngineeringObject)) return e.Cast<object>().Take(50).Select(x => Value(x, depth + 1)).ToList();
        if (o.GetType().Name.Contains("AnonymousType")) return o; // probe-built evidence, already plain
        var d = new Dictionary<string, object> { { "clrType", o.GetType().FullName } };
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0 || p.Name == "Parent") continue;
            d[p.Name] = Safe(() => Value(p.GetValue(o), depth + 1));
        }
        return d;
    }

    internal static object File(string path) => System.IO.File.Exists(path) ? (object)new { path = Program.Rel(path), bytes = new FileInfo(path).Length } : null;

    internal static object Library(GlobalLibrary l) => new
    {
        path = Safe(() => Program.Rel(l.Path.FullName)), name = Safe(() => l.Name), isReadOnly = Safe(() => l.IsReadOnly),
        isModified = Safe(() => l.IsModified), isWriteProtected = Safe(() => l.IsWriteProtected), tree = Safe(() => Tree(l))
    };

    internal static object Tree(ILibrary l) => new { masterCopies = Safe(() => McFolder(l.MasterCopyFolder)), types = Safe(() => TypeFolder(l.TypeFolder)) };

    private static object McFolder(MasterCopyFolder f) => new
    {
        name = Safe(() => f.Name),
        masterCopies = Safe(() => f.MasterCopies.Select(m => new { name = Safe(() => m.Name), contents = Safe(() => m.ContentDescriptions.Select(d => d.ContentName + ":" + d.ContentType).ToList()) }).ToList()),
        folders = Safe(() => f.Folders.Select(McFolder).ToList())
    };

    private static object TypeFolder(LibraryTypeFolder f) => new
    {
        name = Safe(() => f.Name),
        types = Safe(() => f.Types.Select(t => new
        {
            name = Safe(() => t.Name), guid = Safe(() => t.Guid.ToString()),
            versions = Safe(() => t.Versions.Select(v => new
            {
                number = Safe(() => v.VersionNumber.ToString()), state = Safe(() => v.State.ToString()), guid = Safe(() => v.Guid.ToString()),
                isDefault = Safe(() => v.IsDefault), dependencies = Safe(() => v.Dependencies.Select(x => x.Guid.ToString()).ToList())
            }).ToList())
        }).ToList()),
        folders = Safe(() => f.Folders.Select(TypeFolder).ToList())
    };

    internal static object Plc(PlcSoftware p) => p == null ? null : new { name = Safe(() => p.Name), blocks = Safe(() => Blocks(p.BlockGroup, "")), types = Safe(() => PlcTypes(p.TypeGroup, "")) };

    private static List<object> Blocks(PlcBlockGroup g, string path) =>
        g.Blocks.Select(b => (object)new { path, name = Safe(() => b.Name), link = Safe(() => Link(b)) })
            .Concat(g.Groups.SelectMany(s => Blocks(s, path + "/" + s.Name))).ToList();

    private static List<object> PlcTypes(PlcTypeGroup g, string path) =>
        g.Types.Select(t => (object)new { path, name = Safe(() => t.Name), link = Safe(() => Link(t)) })
            .Concat(g.Groups.SelectMany(s => PlcTypes(s, path + "/" + s.Name))).ToList();

    internal static object Link(IEngineeringServiceProvider o)
    {
        var info = o.GetService<LibraryTypeInstanceInfo>();
        if (info == null) return null;
        var v = info.LibraryTypeVersion;
        return new { type = Safe(() => v.TypeObject.Name), number = Safe(() => v.VersionNumber.ToString()), version = Safe(() => v.Guid.ToString()), state = Safe(() => v.State.ToString()) };
    }

    internal static object Files(Ctx c)
    {
        var outFiles = Directory.Exists(c.OutDir) ? Directory.GetFiles(c.OutDir, "*", SearchOption.AllDirectories).Select(File).ToList() : new List<object>();
        var libTop = Directory.Exists(c.LibDir) ? Directory.GetFiles(c.LibDir).Select(File).ToList() : new List<object>();
        return new { outDir = outFiles, libraryFolderTopLevel = libTop };
    }
}

internal sealed class Runner
{
    private readonly Options options;
    private TiaPortal portal;
    private readonly List<object> confirmations = new List<object>();
    internal Runner(Options options) { this.options = options; }

    internal int Run()
    {
        Program.StartRequest("portal-processes");
        var process = TiaPortal.GetProcesses().SingleOrDefault(p => p.Id == options.Pid)
            ?? throw new InvalidOperationException("Exact Portal PID not found; no Portal was started.");
        Program.Progress("attach");
        using (portal = process.Attach())
        {
            if (portal.GetCurrentProcess().Id != options.Pid) throw new InvalidOperationException("Attach returned a different Portal PID.");
            portal.Confirmation += OnConfirmation;
            try
            {
                Program.Progress("idle-check");
                if (portal.Projects.Count != 0) throw new InvalidOperationException("Portal must have no open project; close it first. Nothing was changed.");
                if (portal.GlobalLibraries.OfType<UserGlobalLibrary>().Any(l => Program.Under(l.Path.FullName, options.Root)))
                    throw new InvalidOperationException("A library below --disposable-root is open; close it first. Nothing was changed.");
                Program.Emit(new { type = "attached", portalPid = options.Pid, assembly = typeof(TiaPortal).Assembly.FullName, step7 = typeof(PlcSoftware).Assembly.FullName });
                if (options.CheckFixture) return new FixtureCheck(this).Run();
                foreach (var id in options.Cases)
                    if (!RunCase(Catalog.All.Single(c => c.Id == id))) return 2;
                Program.Emit(new { type = "complete", cases = options.Cases.Length });
                return 0;
            }
            finally { portal.Confirmation -= OnConfirmation; }
        }
    }

    // Records every Portal confirmation and answers the most conservative available choice.
    private void OnConfirmation(object sender, ConfirmationEventArgs e)
    {
        var choices = e.Choices.ToString();
        var result = choices.Contains("No") ? ConfirmationResult.No : choices.Contains("Cancel") ? ConfirmationResult.Cancel : ConfirmationResult.Ok;
        confirmations.Add(new { phase = Program.CurrentPhase, caption = e.Caption, text = e.Text, detail = e.DetailText, choices, answered = result.ToString() });
        e.Result = result;
    }

    internal Ctx NewCtx(string id)
    {
        var c = new Ctx { Portal = portal, CaseDir = Path.Combine(options.Work, id) };
        c.OutDir = Path.Combine(c.CaseDir, "out");
        c.LibDir = Path.Combine(c.CaseDir, "lib", Names.Lib);
        c.LibFile = Path.Combine(c.LibDir, Names.Lib + ".al21");
        return c;
    }

    internal void Prepare(Ctx c, bool project, LibMode library)
    {
        c.At("prepare:copy-library");
        Directory.CreateDirectory(c.OutDir);
        CopyDir(options.LibraryDir, c.LibDir);
        if (project)
        {
            c.At("prepare:copy-project");
            var projDir = Path.Combine(c.CaseDir, "proj", "P");
            CopyDir(options.ProjectDir, projDir);
            c.At("prepare:Projects.Open");
            c.Project = portal.Projects.Open(new FileInfo(Path.Combine(projDir, Path.GetFileName(options.ProjectFile))));
            c.Plc1 = FindPlc(c.Project, Names.Plc1);
            c.Plc2 = FindPlc(c.Project, Names.Plc2);
        }
        if (library != LibMode.None)
        {
            c.At("prepare:GlobalLibraries.Open(" + library + ")");
            c.Lib = portal.GlobalLibraries.Open(new FileInfo(c.LibFile), library == LibMode.ReadOnly ? OpenMode.ReadOnly : OpenMode.ReadWrite);
        }
    }

    private bool RunCase(Case k)
    {
        Program.StartRequest(k.Id + ".start");
        confirmations.Clear();
        var rec = new Dictionary<string, object> { { "type", "case" }, { "case", k.Id }, { "question", k.Question }, { "startedUtc", DateTime.UtcNow.ToString("O") } };
        var c = NewCtx(k.Id);
        bool fatal = false, prepared = false;
        try { Prepare(c, k.Project, k.Library); prepared = true; }
        catch (Exception ex) { rec["prepareError"] = Program.Error(ex); rec["prepareStage"] = c.Stage; fatal = Program.IsFatal(ex); }

        if (prepared)
        {
            bool setupOk = true;
            if (k.Setup != null)
            {
                try { c.At("setup"); k.Setup(c); }
                catch (Exception ex) { setupOk = false; rec["setupError"] = Program.Error(ex); rec["setupStage"] = c.Stage; fatal = Program.IsFatal(ex); }
            }
            if (!fatal) rec["before"] = Snapshot(c);
            if (setupOk && !fatal)
            {
                try { c.At("act"); k.Act(c); rec["nativeReturned"] = true; }
                catch (Exception ex)
                {
                    rec["nativeReturned"] = false;
                    rec["exception"] = Program.Error(ex);
                    rec["failureStage"] = c.Stage;
                    rec["isEngineeringException"] = ex is EngineeringException;
                    rec["isEngineeringTargetInvocation"] = ex is EngineeringTargetInvocationException;
                    rec["isProbeSetup"] = ex is ProbeSetupException;
                    fatal = Program.IsFatal(ex);
                }
                if (k.Inspect != null && !fatal)
                {
                    try { k.Inspect(c); }
                    catch (Exception ex) { rec["inspectError"] = Program.Error(ex); rec["inspectStage"] = c.Stage; fatal = Program.IsFatal(ex); }
                }
            }
            if (!fatal) rec["after"] = Snapshot(c);
            rec["notes"] = c.Notes;
        }
        rec["confirmationsDuringCase"] = confirmations.ToList();
        if (fatal) { rec["stopped"] = "Portal loss; no cleanup or retry attempted."; Program.Emit(rec); return false; }

        var cleanup = Cleanup(c, out bool clean);
        rec["cleanup"] = cleanup;
        if (clean && c != null && File.Exists(c.LibFile)) rec["persisted"] = Persisted(c, ref clean);
        rec["confirmationsTotal"] = confirmations.ToList();
        rec["endedUtc"] = DateTime.UtcNow.ToString("O");
        if (!clean) rec["stopped"] = "Cleanup did not leave the Portal idle; no later case attempted.";
        else if (!prepared) rec["stopped"] = "Case preparation failed; check the fixture and environment.";
        Program.Emit(rec);
        return clean && prepared;
    }

    private object Snapshot(Ctx c) => new
    {
        libraries = Describe.Safe(() => portal.GlobalLibraries.OfType<UserGlobalLibrary>().Where(l => Program.Under(l.Path.FullName, c.CaseDir)).Select(Describe.Library).ToList()),
        projectLibrary = c.Project == null ? null : Describe.Safe(() => Describe.Tree(c.PL)),
        plcs = c.Project == null ? null : new[] { Describe.Safe(() => Describe.Plc(c.Plc1)), Describe.Safe(() => Describe.Plc(c.Plc2)) },
        files = Describe.Safe(() => Describe.Files(c))
    };

    // Closes everything the case opened. Each case runs on its own copies, so a Save fallback only touches disposable files.
    internal List<object> Cleanup(Ctx c, out bool clean)
    {
        var log = new List<object>();
        clean = true;
        foreach (var lib in portal.GlobalLibraries.OfType<UserGlobalLibrary>().Where(l => Program.Under(l.Path.FullName, c.CaseDir)).ToList())
        {
            var path = Program.Rel(lib.Path.FullName);
            try { c.At("cleanup:UserGlobalLibrary.Close"); lib.Close(); log.Add(new { library = path, closed = "Close" }); }
            catch (Exception ex) when (!Program.IsFatal(ex))
            {
                try { c.At("cleanup:UserGlobalLibrary.Save+Close"); lib.Save(); lib.Close(); log.Add(new { library = path, closed = "Save+Close (persisted state includes case changes)", closeError = Program.Error(ex) }); }
                catch (Exception ex2) when (!Program.IsFatal(ex2)) { clean = false; log.Add(new { library = path, closed = "failed", closeError = Program.Error(ex), saveCloseError = Program.Error(ex2) }); }
            }
        }
        if (c.Project != null)
        {
            try { c.At("cleanup:Project.Close"); c.Project.Close(); log.Add(new { project = "closed without save" }); }
            catch (Exception ex) when (!Program.IsFatal(ex)) { clean = false; log.Add(new { project = "close failed", error = Program.Error(ex) }); }
        }
        if (portal.Projects.Count != 0 || portal.GlobalLibraries.OfType<UserGlobalLibrary>().Any(l => Program.Under(l.Path.FullName, c.CaseDir))) clean = false;
        return log;
    }

    // Reopens the case's library copy read-only to show what actually reached disk.
    private object Persisted(Ctx c, ref bool clean)
    {
        try
        {
            c.At("persisted:Open(ReadOnly)");
            var lib = portal.GlobalLibraries.Open(new FileInfo(c.LibFile), OpenMode.ReadOnly);
            var snapshot = Describe.Library(lib);
            c.At("persisted:Close");
            lib.Close();
            return snapshot;
        }
        catch (Exception ex) when (!Program.IsFatal(ex))
        {
            clean = !portal.GlobalLibraries.OfType<UserGlobalLibrary>().Any(l => Program.Under(l.Path.FullName, c.CaseDir));
            return new { persistedError = Program.Error(ex) };
        }
    }

    internal static PlcSoftware FindPlc(Project project, string name)
    {
        var matches = AllDevices(project).SelectMany(d => AllItems(d.DeviceItems))
            .Select(i => i.GetService<SoftwareContainer>()?.Software as PlcSoftware)
            .Where(p => p != null && p.Name == name).ToList();
        if (matches.Count != 1) throw new ProbeSetupException("Expected exactly one PLC software named " + name + ", found " + matches.Count + ".");
        return matches[0];
    }

    private static IEnumerable<Device> AllDevices(Project p) =>
        p.Devices.Concat(p.UngroupedDevicesGroup.Devices).Concat(GroupDevices(p.DeviceGroups)).Distinct();

    private static IEnumerable<Device> GroupDevices(IEnumerable<DeviceUserGroup> groups) =>
        groups.SelectMany(g => g.Devices.Concat(GroupDevices(g.Groups)));

    private static IEnumerable<DeviceItem> AllItems(IEnumerable<DeviceItem> items) =>
        items.SelectMany(i => new[] { i }.Concat(AllItems(i.DeviceItems)));

    internal static void CopyDir(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source)) CopyDir(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}

// Read-only check that a copy of the fixture matches what the cases assume.
internal sealed class FixtureCheck
{
    private readonly Runner runner;
    private readonly List<object> checks = new List<object>();
    private bool pass = true;
    internal FixtureCheck(Runner runner) { this.runner = runner; }

    private void Check(string what, Func<bool> test)
    {
        try { var ok = test(); pass &= ok; checks.Add(new { what, pass = ok }); }
        catch (Exception ex) when (!Program.IsFatal(ex)) { pass = false; checks.Add(new { what, pass = false, error = ex.Message }); }
    }

    internal int Run()
    {
        Program.StartRequest("fixture-check");
        var c = runner.NewCtx("fixture-check");
        try { runner.Prepare(c, true, LibMode.ReadOnly); }
        catch (Exception ex) when (!Program.IsFatal(ex))
        {
            var log = runner.Cleanup(c, out bool cleaned);
            Program.Emit(new { type = "fixture-check", pass = false, prepareStage = c.Stage, prepareError = Program.Error(ex), cleanup = log, clean = cleaned });
            return 3;
        }
        var gl = c.Lib;
        bool Released(LibraryType t) => t.Versions.Count == 1 && F.Version(t, "1.0.0").State == LibraryTypeVersionState.Committed && F.Version(t, "1.0.0").IsDefault;
        bool DependsOn(LibraryType user, LibraryType dep) => F.Version(user, "1.0.0").Dependencies.Any(d => d.TypeObject.Guid == dep.Guid);
        string LinkedTo(object o) { var i = ((IEngineeringServiceProvider)o).GetService<LibraryTypeInstanceInfo>(); return i == null ? null : i.LibraryTypeVersion.TypeObject.Guid + "@" + i.LibraryTypeVersion.VersionNumber; }

        Check("global library name is " + Names.Lib, () => gl.Name == Names.Lib);
        Check("global root types: LP_Udt and LP_Fb, each only 1.0.0 released default", () => gl.TypeFolder.Types.Find(Names.Udt) != null && gl.TypeFolder.Types.Find(Names.Fb) != null && Released(F.Type(gl, Names.Udt)) && Released(F.Type(gl, Names.Fb)));
        Check("global LP_Fb 1.0.0 depends on LP_Udt", () => DependsOn(F.Type(gl, Names.Fb), F.Type(gl, Names.Udt)));
        Check("global folder LP_TypeFolder holds LP_GUdt and LP_GFb, each only 1.0.0 released default", () =>
        {
            var folder = gl.TypeFolder.Folders.Find(Names.TypeFolder);
            return folder != null && folder.Types.Find(Names.GUdt) != null && folder.Types.Find(Names.GFb) != null && Released(F.Type(gl, Names.GUdt)) && Released(F.Type(gl, Names.GFb));
        });
        Check("global LP_GFb 1.0.0 depends on LP_GUdt", () => DependsOn(F.Type(gl, Names.GFb), F.Type(gl, Names.GUdt)));
        Check("global root master copy LP_Free exists", () => gl.MasterCopyFolder.MasterCopies.Find(Names.Free) != null);
        Check("global master-copy folder LP_McFolder exists and is not empty", () => { var f = gl.MasterCopyFolder.Folders.Find(Names.McFolder); return f != null && (f.MasterCopies.Count > 0 || f.Folders.Count > 0); });
        Check("project library root types LP_Udt and LP_Fb, each only 1.0.0 released default, same GUIDs as global", () =>
            c.PL.TypeFolder.Types.Find(Names.Udt) != null && c.PL.TypeFolder.Types.Find(Names.Fb) != null
            && Released(F.Type(c.PL, Names.Udt)) && Released(F.Type(c.PL, Names.Fb))
            && F.Type(c.PL, Names.Udt).Guid == F.Type(gl, Names.Udt).Guid && F.Type(c.PL, Names.Fb).Guid == F.Type(gl, Names.Fb).Guid);
        Check("project library has no LP_GUdt or LP_GFb type", () => F.Types(c.PL.TypeFolder).All(t => t.Name != Names.GUdt && t.Name != Names.GFb));
        Check("project library root master copy LP_Free exists", () => c.PL.MasterCopyFolder.MasterCopies.Find(Names.Free) != null);
        Check("PLC_1 LP_Udt is an instance of project LP_Udt 1.0.0", () => LinkedTo(F.PlcTypeOf(c.Plc1, Names.Udt)) == F.Type(c.PL, Names.Udt).Guid + "@1.0.0");
        Check("PLC_1 LP_Fb is an instance of project LP_Fb 1.0.0", () => LinkedTo(F.Block(c.Plc1, Names.Fb)) == F.Type(c.PL, Names.Fb).Guid + "@1.0.0");
        Check("PLC_1 LP_Free block, LP_PlainUdt and LP_GUdt types exist and are not type instances", () =>
            LinkedTo(F.Block(c.Plc1, Names.Free)) == null && LinkedTo(F.PlcTypeOf(c.Plc1, Names.PlainUdt)) == null && LinkedTo(F.PlcTypeOf(c.Plc1, Names.GUdt)) == null);
        Check("PLC_2 has no LP_ objects", () =>
            F.BlockNames(c.Plc2.BlockGroup).Concat(F.TypeNames(c.Plc2.TypeGroup)).All(n => !n.StartsWith("LP_", StringComparison.Ordinal)));

        var cleanup = runner.Cleanup(c, out bool clean);
        Program.Emit(new { type = "fixture-check", pass, checks, cleanup, clean });
        return pass && clean ? 0 : 3;
    }
}

internal static class SelfTest
{
    internal static int Run()
    {
        var ids = Catalog.All.Select(c => c.Id).ToList();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw new Exception("Duplicate case id.");
        if (Catalog.All.Any(c => c.Act == null || string.IsNullOrWhiteSpace(c.Question))) throw new Exception("Case without act or question.");

        var root = Path.Combine(Path.GetTempPath(), "lwp-selftest-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "proj", "Fixture.ap21");
        var library = Path.Combine(root, Names.Lib, Names.Lib + ".al21");
        Directory.CreateDirectory(Path.GetDirectoryName(project));
        Directory.CreateDirectory(Path.GetDirectoryName(library));
        File.WriteAllText(project, "");
        File.WriteAllText(library, "");
        try
        {
            string[] Args(params string[] extra) =>
                new[] { "--pid", "1", "--disposable-root", root, "--project", project, "--library", library, "--work", Path.Combine(root, "work") }.Concat(extra).ToArray();
            Options.Parse(Args("--confirm-disposable", "--all"));
            Options.Parse(Args("--confirm-disposable", "--case", "G1,T17"));
            Options.Parse(Args("--confirm-disposable", "--check-fixture"));
            var rejections = new[]
            {
                Args("--all"),
                Args("--confirm-disposable"),
                Args("--confirm-disposable", "--all", "--check-fixture"),
                Args("--confirm-disposable", "--case", "X99"),
                Args("--confirm-disposable", "--all", "--output", Path.Combine(root, "elsewhere.jsonl")),
                Args("--confirm-disposable", "--all", "--work", Path.GetTempPath()),
                Args("--confirm-disposable", "--all", "--work", Path.Combine(root, "proj", "work")),
                Args("--confirm-disposable", "--all", "--library", project),
            };
            int rejected = 0;
            foreach (var a in rejections) { try { Options.Parse(a); } catch (ArgumentException) { rejected++; } }
            if (rejected != rejections.Length) throw new Exception("Scope rejection self-test failed: " + rejected + "/" + rejections.Length);
            Program.Emit(new { type = "self-test", success = true, cases = ids.Count, scopeRejections = rejected, attached = false, is64BitProcess = Environment.Is64BitProcess });
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
}
