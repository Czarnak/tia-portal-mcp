using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;

// Native library-types probe (issue #218 PR 3, Task 0). Every case calls Siemens Openness directly with
// no worker pre-checks, on fresh per-case copies of a disposable project and global library.
// Framework (arguments, guards, attach, originals, copies, snapshots, records, watchdog, cleanup) is the
// PR 2 library-write probe's; the cases and the fixture check are new.
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
            if (args.Length == 1 && args[0] == "--list")
            {
                Emit(new { cases = Catalog.All.Select(c => new { id = c.Id, question = c.Question, crashRisk = c.Hazard != null, inAll = c.Hazard == null }) });
                return 0;
            }
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest.Run();
            var options = Options.Parse(args);
            InitRedactions(options);
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
            var line = Scrub(Json.Serialize(value));
            output?.WriteLine(line);
            Console.WriteLine(line);
            Console.Out.Flush();
        }
    }

    // Results may be committed as evidence: every emitted line has the root, profile, project, library and
    // user names replaced (plain and JSON-escaped, ignoring case; names only as whole words).
    private static readonly List<Tuple<Regex, string>> Redactions = new List<Tuple<Regex, string>>();

    internal static void InitRedactions(Options o)
    {
        Redactions.Clear();
        AddRedaction(o.Root.TrimEnd('\\'), "<root>", false);
        AddRedaction(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<profile>", false);
        AddRedaction(Path.GetFileNameWithoutExtension(o.ProjectFile), "<proj>", true);
        AddRedaction(o.LibName, "<lib>", true);
        AddRedaction(Environment.UserName, "<user>", true);
    }

    private static void AddRedaction(string text, string label, bool word)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var form in new[] { text, text.Replace("\\", "\\\\") }.Distinct())
        {
            var pattern = Regex.Escape(form);
            if (word) pattern = "(?<![A-Za-z0-9_])" + pattern + "(?![A-Za-z0-9_])";
            Redactions.Add(Tuple.Create(new Regex(pattern, RegexOptions.IgnoreCase), label));
        }
    }

    internal static string Scrub(string line) => Redactions.Aggregate(line, (s, r) => r.Item1.Replace(s, r.Item2));

    internal static bool InGitTree(string path)
    {
        for (var d = new DirectoryInfo(path); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git"))) return true;
        return false;
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
    // Fixture roles (the user prepares them; --check-fixture verifies every row on a copy).
    internal const string Plc1 = "PLC_1", Plc2 = "PLC_2", Prefix = "LT_";
    internal const string Udt = "LT_Udt", UdtUser = "LT_UdtUser_DB", Fb = "LT_Fb", FbDb = "LT_Fb_DB", Caller = "LT_Caller";
    internal const string FbUdt = "LT_FbUdt", FbKhp = "LT_FbKhp", GlobalType = "LT_GlobalType", Fresh = "LT_Fresh";
}

internal sealed class Options
{
    internal int Pid;
    internal string Root, ProjectFile, LibraryFile, Work, OutputPath;
    internal string[] Cases = new string[0];
    internal bool CheckFixture;
    internal string ProjectDir => Path.GetDirectoryName(ProjectFile);
    internal string LibraryDir => Path.GetDirectoryName(LibraryFile);
    internal string LibName => Path.GetFileNameWithoutExtension(LibraryFile);

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
        if (Program.InGitTree(o.Root)) throw new ArgumentException("--disposable-root must not be inside a git work tree (copies could be committed).");
        if (o.ProjectFile == null || !File.Exists(o.ProjectFile) || !o.ProjectFile.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase) || !Program.Under(o.ProjectFile, o.Root))
            throw new ArgumentException("--project must be an existing .ap21 inside --disposable-root.");
        if (o.LibraryFile == null || !File.Exists(o.LibraryFile) || !o.LibraryFile.EndsWith(".al21", StringComparison.OrdinalIgnoreCase) || !Program.Under(o.LibraryFile, o.Root))
            throw new ArgumentException("--library must be an existing .al21 inside --disposable-root.");
        if (!string.Equals(o.LibName, Path.GetFileName(o.LibraryDir), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--library must be <Name>\\<Name>.al21 (library folder and file share the name).");
        // Short names would make the whole-word redaction hit ordinary words in the records.
        if (Path.GetFileNameWithoutExtension(o.ProjectFile).Length < 6 || o.LibName.Length < 6)
            throw new ArgumentException("Project and library names must be at least 6 characters (use distinctive role-style names).");
        if (string.Equals(o.ProjectDir, o.Root, StringComparison.OrdinalIgnoreCase) || string.Equals(o.LibraryDir, o.Root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(o.ProjectDir, o.LibraryDir, StringComparison.OrdinalIgnoreCase)
            || Program.Under(o.ProjectDir, o.LibraryDir) || Program.Under(o.LibraryDir, o.ProjectDir))
            throw new ArgumentException("Project and library must live in separate folders below --disposable-root.");
        if (o.Work == null || !Program.Under(o.Work, o.Root) || Directory.Exists(o.Work) || File.Exists(o.Work)
            || Program.Under(o.Work, o.ProjectDir) || Program.Under(o.Work, o.LibraryDir))
            throw new ArgumentException("--work must be a new path inside --disposable-root, outside the project and library folders.");
        if ((all ? 1 : 0) + (selected != null ? 1 : 0) + (o.CheckFixture ? 1 : 0) != 1)
            throw new ArgumentException("Specify exactly one of --all, --case IDS or --check-fixture.");
        if (all) o.Cases = Catalog.All.Where(c => c.Hazard == null).Select(c => c.Id).ToArray();
        if (selected != null)
        {
            o.Cases = selected.Split(',').Select(s => s.Trim()).ToArray();
            var unknown = o.Cases.Where(id => Catalog.All.All(c => c.Id != id)).ToArray();
            if (unknown.Length != 0 || o.Cases.Length == 0) throw new ArgumentException("Unknown case: " + string.Join(",", unknown));
            if (o.Cases.Distinct().Count() != o.Cases.Length) throw new ArgumentException("A case is named more than once.");
            if (o.Cases.Length > 1 && o.Cases.Any(id => Catalog.All.Single(c => c.Id == id).Hazard != null))
                throw new ArgumentException("A crash-risk case runs alone: name exactly one per run.");
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

// Thrown on purpose inside an open transaction to observe the rollback (P1r cases).
internal sealed class DeliberateRollbackException : Exception
{
    internal DeliberateRollbackException(string message) : base(message) { }
}

internal sealed class Case
{
    internal string Id, Question;
    internal bool Project;
    internal LibMode Library;
    internal Action<Ctx> Setup, Act, Inspect;
    internal string Hazard;
}

internal sealed class Ctx
{
    internal const CreateOrReleaseDependenciesMode NoDeps = CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies;
    internal TiaPortal Portal;
    internal Project Project;
    internal UserGlobalLibrary Lib, Extra;
    internal PlcSoftware Plc1, Plc2;
    internal string CaseDir, OutDir, LibDir, LibFile, LibName;
    internal string Stage = "prepare";
    internal bool Held;                   // a Transaction/ExclusiveAccess Dispose failed: either may still be held
    internal DirectoryInfo Docs;          // last setup export, consumed by the act
    internal LibraryType Made;            // type created by MakeTypeInPlc2
    internal LibraryTypeVersion Working;  // current in-work version from setup
    internal LibraryTypeFolder Folder;    // nested type folder made in setup (P1n)
    private int exports;
    private List<object> steps;
    internal readonly Dictionary<string, object> Notes = new Dictionary<string, object>();
    internal ProjectLibrary PL => Project.ProjectLibrary;
    internal void At(string stage) { Stage = stage; Program.Progress(CaseDir == null ? stage : Path.GetFileName(CaseDir) + "." + stage); }
    internal void Note(string key, object value) { Notes[key] = Describe.Value(value); }
    internal string Out(string relative) => Path.Combine(OutDir, relative);

    // Appends to a list note (setup helpers run more than once per case).
    internal void NoteAdd(string key, object value)
    {
        if (!(Notes.TryGetValue(key, out var existing) && existing is List<object> list)) Notes[key] = list = new List<object>();
        list.Add(Describe.Value(value));
    }

    // Runs one named stage and records its success or exception in notes.steps; rethrows.
    internal T Step<T>(string stage, Func<T> body)
    {
        if (steps == null) Notes["steps"] = steps = new List<object>();
        At(stage);
        try { var r = body(); steps.Add(new { stage, ok = true }); return r; }
        catch (Exception ex) { steps.Add(new { stage, ok = false, error = ex.GetType().FullName, message = ex.Message }); throw; }
    }

    internal void Step(string stage, Action body) => Step<object>(stage, () => { body(); return null; });

    // Exports into a fresh out\<label>[n]\<name>.xml so the directory holds only that file.
    internal DirectoryInfo ExportXml(IEngineeringServiceProvider obj, string name, string label = "docs")
    {
        At("export(" + name + ")");
        var dir = Out(label + (exports++ == 0 ? "" : exports.ToString()));
        Directory.CreateDirectory(dir);
        var file = new FileInfo(Path.Combine(dir, name + ".xml"));
        if (obj is PlcType t) t.Export(file, ExportOptions.None);
        else if (obj is PlcBlock b) b.Export(file, ExportOptions.None);
        else throw new ProbeSetupException(name + " is neither a PlcType nor a PlcBlock.");
        return new DirectoryInfo(dir);
    }

    // PR 2-proven route (T1): export the PLC_1 object, create a project-library type with PLC_2 as test target.
    internal LibraryType MakeTypeInPlc2(string name)
    {
        var dir = ExportXml(F.Obj(Plc1, name), name);
        At("CreateFromDocuments(" + name + " -> " + Names.Plc2 + ")");
        var r = PL.TypeFolder.Types.CreateFromDocuments(dir, name, F.Group(Plc2, name), LibraryImportOptions.None);
        Note("setupCreated", F.Created(r));
        Made = r.CreatedType ?? throw new ProbeSetupException("CreateFromDocuments returned no type.");
        Note("setupInstance", Describe.Link(F.Obj(Plc2, name)));
        Working = F.InWork(Made);
        return Made;
    }

    internal void Release(LibraryTypeVersion version, Version number)
    {
        At("Release(" + number + ")");
        version.Release(NoDeps, number, "probe", "setup");
        NoteAdd("releases", new { number = number.ToString(), sameObjectAfter = Describe.Safe(() => Describe.Ver(version)) });
    }

    internal LibraryTypeVersion EditVia(string name)
    {
        At("Edit(" + name + " via " + Names.Plc2 + " instance)");
        var info = F.Obj(Plc2, name).GetService<LibraryTypeInstanceInfo>() ?? throw new ProbeSetupException(Names.Plc2 + " " + name + " is not a type instance.");
        var inWork = info.LibraryTypeVersion.Edit(info);
        NoteAdd("edits", Describe.Ver(inWork));
        return Working = inWork;
    }

    // Type with released 0.0.1 and 0.0.2 (default expected 0.0.2), PLC_2 instance linked to 0.0.2.
    internal LibraryType Chain(string name)
    {
        MakeTypeInPlc2(name);
        Release(Working, F.V(0, 0, 1));
        Release(EditVia(name), F.V(0, 0, 2));
        Note("chain", F.Versions(F.Type(PL, name)));
        return F.Type(PL, name);
    }

    // Never inside a transaction.
    internal object Compile(IEngineeringServiceProvider target, string label)
    {
        At("Compile(" + label + ")");
        var compilable = target.GetService<ICompilable>() ?? throw new ProbeSetupException(label + " has no ICompilable service.");
        var r = compilable.Compile();
        var flat = Flat(r.Messages, "").ToList();
        // The fixture project may carry unrelated compile errors (the baseline); only messages under an LT_* object matter.
        var lt = flat.Where(x => x.Chain.Contains(Names.Prefix) && x.Message.State.ToString() != "Success").ToList();
        return new
        {
            state = r.State.ToString(), errors = r.ErrorCount, warnings = r.WarningCount, messagesTotal = flat.Count,
            ltErrors = lt.Count(x => x.Message.State.ToString() == "Error"),
            ltMessages = lt.Take(40).Select(x => new { chain = x.Chain, state = x.Message.State.ToString(), description = Describe.Safe(() => x.Message.Description) }).ToList(),
            messages = flat.Where(x => x.Message.State.ToString() != "Success").Take(20)
                .Select(x => new { path = Describe.Safe(() => x.Message.Path), state = x.Message.State.ToString(), description = Describe.Safe(() => x.Message.Description) }).ToList()
        };
    }

    private static IEnumerable<(CompilerResultMessage Message, string Chain)> Flat(IEnumerable<CompilerResultMessage> messages, string parent) =>
        messages.SelectMany(m =>
        {
            var chain = parent.Length == 0 ? PathOf(m) : parent + " > " + PathOf(m);
            return new[] { (m, chain) }.Concat(Flat(m.Messages, chain));
        });

    private static string PathOf(CompilerResultMessage m)
    {
        try { return m.Path ?? ""; }
        catch (Exception ex) when (!Program.IsFatal(ex)) { return "<unreadable>"; }
    }

    // Objects placed by library operations stay inconsistent (not exportable) until compiled (run 1: P4, P8, P12).
    // Records IsConsistent, compiles the PLC, records IsConsistent again. Never inside a transaction.
    internal void CompileFirst(PlcSoftware plc, string name, string key)
    {
        Note(key + "ConsistentBefore", Describe.Safe(() => F.Consistent(F.Obj(plc, name))));
        Note(key + "Compile", Describe.Safe(() => Compile(plc, plc.Name)));
        Note(key + "ConsistentAfter", Describe.Safe(() => F.Consistent(F.Obj(plc, name))));
    }

    internal string ExportFile(PlcSoftware plc, string name, string label) =>
        Path.Combine(ExportXml(F.Obj(plc, name), name, label).FullName, name + ".xml");

    internal object Content(PlcSoftware plc, string name, string label)
    {
        var file = ExportFile(plc, name, label);
        return new { hash = F.ContentHash(file), members = F.MemberNames(file) };
    }

    // plc_write update_type_content's XML route (worker PlcTypeImporter.ImportXml): export, add a top-level
    // member "probeAdded : Int", drop DocumentInfo as plc_read does, PlcTypeComposition.Import(file, Override).
    internal void ImportWithExtraMember(PlcSoftware plc, string name)
    {
        var type = F.Obj(plc, name) as PlcType ?? throw new ProbeSetupException(name + " on " + plc.Name + " is not a PlcType.");
        var group = type.Parent as PlcTypeGroup ?? throw new ProbeSetupException(name + " parent is not a PlcTypeGroup.");
        var doc = XDocument.Load(ExportFile(plc, name, "toEdit"), LoadOptions.PreserveWhitespace);
        var first = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Member") ?? throw new ProbeSetupException("No Member in the " + name + " export.");
        first.AddAfterSelf(new XElement(first.Name.Namespace + "Member", new XAttribute("Name", "probeAdded"), new XAttribute("Datatype", "Int")));
        doc.Root?.Elements().Where(e => e.Name.LocalName == "DocumentInfo").Remove();
        Directory.CreateDirectory(Out("edited"));
        var edited = Path.Combine(Out("edited"), name + ".xml");
        doc.Save(edited, SaveOptions.DisableFormatting);
        var imported = Step("PlcTypeComposition.Import(Override)", () => group.Types.Import(new FileInfo(edited), ImportOptions.Override));
        Note("importedTypes", Describe.Safe(() => imported.Select(x => x.Name).ToList()));
    }

    // Members whose XML Datatype names a user type (quoted), read from a fresh export.
    internal object UserTypedMembers(PlcSoftware plc, string name) =>
        F.UserTypedMembers(Path.Combine(ExportXml(F.Obj(plc, name), name, "inspect").FullName, name + ".xml"));

    internal object State(string name) => new
    {
        libraryTypes = Describe.Safe(() => F.Named(PL, name)),
        plc1 = Describe.Safe(() => F.Presence(Plc1, name)),
        plc2 = Describe.Safe(() => F.Presence(Plc2, name)),
        projectMasterCopies = Describe.Safe(() => PL.MasterCopyFolder.MasterCopies.Select(m => m.Name).ToList())
    };
}

// Native finders. A missing fixture object is a setup error, not a probe observation.
internal static class F
{
    internal static Version V(int a, int b, int c) => new Version(a, b, c);

    internal static IEnumerable<LibraryType> Types(LibraryTypeFolder folder) =>
        folder.Types.Concat(folder.Folders.SelectMany(Types));

    internal static IEnumerable<MasterCopy> MasterCopies(MasterCopyFolder folder) =>
        folder.MasterCopies.Concat(folder.Folders.SelectMany(MasterCopies));

    internal static LibraryType Type(ILibrary library, string name)
    {
        var matches = Types(library.TypeFolder).Where(t => t.Name == name).ToList();
        if (matches.Count != 1) throw new ProbeSetupException("Expected exactly one library type " + name + ", found " + matches.Count + ".");
        return matches[0];
    }

    internal static object Named(ILibrary library, string name) =>
        Types(library.TypeFolder).Where(t => t.Name == name).Select(t => new { guid = Describe.Safe(() => t.Guid.ToString()), versions = Describe.Safe(() => Versions(t)) }).ToList();

    internal static List<object> Versions(LibraryType t) => t.Versions.Select(Describe.Ver).ToList();

    internal static LibraryTypeVersion Default(LibraryType type) =>
        type.Versions.SingleOrDefault(v => v.IsDefault) ?? throw new ProbeSetupException("No single default version on " + type.Name + ".");

    internal static LibraryTypeVersion InWork(LibraryType type) =>
        type.Versions.SingleOrDefault(v => v.State == LibraryTypeVersionState.InWork) ?? throw new ProbeSetupException("No single in-work version on " + type.Name + ".");

    internal static LibraryTypeVersion Find(LibraryType type, Version number) =>
        type.Versions.Find(number) ?? throw new ProbeSetupException("No version " + number + " on " + type.Name + ".");

    internal static bool IsType(string name) => name == Names.Udt || name == Names.GlobalType;

    internal static IEngineeringServiceProvider TryObj(PlcSoftware plc, string name) =>
        IsType(name) ? (IEngineeringServiceProvider)FindType(plc.TypeGroup, name) : FindBlock(plc.BlockGroup, name);

    internal static IEngineeringServiceProvider Obj(PlcSoftware plc, string name) =>
        TryObj(plc, name) ?? throw new ProbeSetupException("Missing " + name + " on " + plc.Name + ".");

    internal static IEngineeringObject Group(PlcSoftware plc, string name) => IsType(name) ? (IEngineeringObject)plc.TypeGroup : plc.BlockGroup;

    internal static void Delete(IEngineeringServiceProvider o)
    {
        if (o is PlcType t) t.Delete();
        else ((PlcBlock)o).Delete();
    }

    internal static object Presence(PlcSoftware plc, string name)
    {
        var o = TryObj(plc, name);
        return o == null ? (object)new { present = false } : new { present = true, link = Describe.Link(o) };
    }

    internal static PlcBlock Block(PlcSoftware plc, string name) => FindBlock(plc.BlockGroup, name) ?? throw new ProbeSetupException("Missing block " + name + " on " + plc.Name + ".");
    private static PlcBlock FindBlock(PlcBlockGroup group, string name) =>
        group.Blocks.Find(name) ?? group.Groups.Select(g => FindBlock(g, name)).FirstOrDefault(b => b != null);

    private static PlcType FindType(PlcTypeGroup group, string name) =>
        group.Types.Find(name) ?? group.Groups.Select(g => FindType(g, name)).FirstOrDefault(t => t != null);

    internal static IEnumerable<string> BlockNames(PlcBlockGroup g) => g.Blocks.Select(b => b.Name).Concat(g.Groups.SelectMany(BlockNames));
    internal static IEnumerable<string> TypeNames(PlcTypeGroup g) => g.Types.Select(t => t.Name).Concat(g.Groups.SelectMany(TypeNames));

    internal static IMasterCopySource Source(object o) => o as IMasterCopySource ?? throw new ProbeSetupException(o.GetType().Name + " is not an IMasterCopySource.");

    internal static T As<T>(LibraryTypeVersion v) where T : class => v as T ?? throw new ProbeSetupException("Version is " + v.GetType().FullName + ", not " + typeof(T).Name + ".");

    internal static object Created(TypeCreateTransferResults r) => new
    {
        transferResultState = Describe.Safe(() => r.TransferResultState.ToString()),
        messages = Describe.Safe(() => r.Messages.Select(m => Describe.Value(m.Message)).ToList()),
        type = Describe.Safe(() => r.CreatedType == null ? null : (object)new { name = r.CreatedType.Name, guid = r.CreatedType.Guid.ToString(), versions = Versions(r.CreatedType) })
    };

    internal static object LinkCheck(IEngineeringServiceProvider o, LibraryType created)
    {
        var info = o.GetService<LibraryTypeInstanceInfo>();
        if (info == null) return new { linked = false };
        var v = info.LibraryTypeVersion;
        return new
        {
            linked = true, link = Describe.Link(o),
            typeIsCreatedType = Describe.Safe(() => created != null && v.TypeObject.Guid == created.Guid),
            versionInCreatedType = Describe.Safe(() => created != null && created.Versions.Any(x => x.Guid == v.Guid))
        };
    }

    private static IEnumerable<XElement> Local(this IEnumerable<XElement> e, string name) => e.Where(x => x.Name.LocalName == name);

    internal static object UserTypedMembers(string xmlPath) =>
        XDocument.Load(xmlPath).Descendants().Local("Member").Where(m => ((string)m.Attribute("Datatype") ?? "").Contains("\"")).Select(m => new
        {
            section = (string)m.Ancestors().Local("Section").FirstOrDefault()?.Attribute("Name"),
            name = (string)m.Attribute("Name"), datatype = (string)m.Attribute("Datatype")
        }).ToList();

    internal static bool HasMember(string xmlPath, string datatype, params string[] sections) =>
        XDocument.Load(xmlPath).Descendants().Local("Member").Any(m => (string)m.Attribute("Datatype") == datatype
            && (sections.Length == 0 || sections.Contains((string)m.Ancestors().Local("Section").FirstOrDefault()?.Attribute("Name"))));

    internal static object Consistent(IEngineeringServiceProvider o) =>
        o is PlcBlock b ? (object)b.IsConsistent : o is PlcType t ? (object)t.IsConsistent : "not a PlcBlock or PlcType";

    internal static List<string> MemberNames(string xmlPath) =>
        XDocument.Load(xmlPath).Descendants().Local("Member").Select(m => (string)m.Attribute("Name") + ":" + (string)m.Attribute("Datatype")).ToList();

    // SHA-256 of the export without DocumentInfo (it carries the export time).
    internal static string ContentHash(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        doc.Root?.Elements().Local("DocumentInfo").Remove();
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting)))).Replace("-", "");
    }

    internal static bool AnyUserType(string xmlPath) =>
        XDocument.Load(xmlPath).Descendants().Local("Member").Any(m => ((string)m.Attribute("Datatype") ?? "").Contains("\""));

    // FB call with its instance DB, in LAD/FBD or SCL XML: CallInfo Name=<fb> with a descendant Name=<db>.
    internal static bool Calls(string xmlPath, string fb, string db) =>
        XDocument.Load(xmlPath).Descendants().Local("CallInfo").Any(ci => (string)ci.Attribute("Name") == fb
            && ci.Descendants().Any(d => (string)d.Attribute("Name") == db));

    // Block-level Title (else Comment) text element: Document > SW.Blocks.* > ObjectList > MultilingualText > ... > Text.
    internal static XElement BlockTitleText(XDocument doc)
    {
        var block = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal));
        var texts = block?.Elements().Local("ObjectList").Elements().Local("MultilingualText").ToList() ?? new List<XElement>();
        var owner = texts.FirstOrDefault(t => (string)t.Attribute("CompositionName") == "Title") ?? texts.FirstOrDefault(t => (string)t.Attribute("CompositionName") == "Comment");
        return owner?.Descendants().Local("Text").FirstOrDefault();
    }
}

// The P1 conversion route: export (setup) -> [ExclusiveAccess + Transaction(project)] -> delete original
// -> CreateFromDocuments into the same PLC group -> link check -> CommitOnDispose -> dispose.
internal static class Route
{
    // folder: target type folder, the project library's root when null.
    internal static void Convert(Ctx c, PlcSoftware plc, string name, bool transaction, string throwAfter, LibraryTypeFolder folder = null)
    {
        ExclusiveAccess access = null;
        Transaction tx = null;
        var completed = false;
        try
        {
            if (transaction)
            {
                access = c.Step("TiaPortal.ExclusiveAccess", () => c.Portal.ExclusiveAccess("library-types probe"));
                tx = c.Step("ExclusiveAccess.Transaction(project)", () => access.Transaction(c.Project, "library-types probe " + name));
            }
            c.Step("delete-original", () => F.Delete(F.Obj(plc, name)));
            if (throwAfter == "delete") throw new DeliberateRollbackException("Deliberate failure after the delete, before the import.");
            var r = c.Step("CreateFromDocuments", () => (folder ?? c.PL.TypeFolder).Types.CreateFromDocuments(c.Docs, name, F.Group(plc, name), LibraryImportOptions.None));
            c.Note("created", F.Created(r));
            // Record only: a missing or renamed instance must not skip the commit.
            c.Note("link", c.Step("link-check", () => Describe.Safe(() => F.LinkCheck(F.Obj(plc, name), r.CreatedType))));
            if (throwAfter == "import") throw new DeliberateRollbackException("Deliberate failure after the import and link check, before commit.");
            if (transaction) c.Step("Transaction.CommitOnDispose", () => tx.CommitOnDispose());
            completed = true;
        }
        finally
        {
            // The commit happens in Dispose: on the success path its failure must surface as the act's exception,
            // and a later ExclusiveAccess failure must not replace it. Any Dispose failure sets c.Held (run stops).
            try { if (tx != null) Dispose(c, "Transaction.Dispose", tx, completed); }
            finally { if (access != null) Dispose(c, "ExclusiveAccess.Dispose", access, completed && !c.Held); }
        }
    }

    private static void Dispose(Ctx c, string stage, IDisposable d, bool surface)
    {
        var keep = c.Stage;
        // Not surfacing means another exception is propagating: a successful Dispose must not relabel its stage.
        try { c.Step(stage, d.Dispose); if (!surface) c.Stage = keep; }
        catch (Exception ex) when (!Program.IsFatal(ex))
        {
            c.Held = true;
            if (surface) throw;
            c.Stage = keep; // another exception is propagating; keep its stage
        }
    }

    internal static void Inspect(Ctx c, PlcSoftware plc, string name, bool compile)
    {
        c.At("inspect");
        c.Note("stateAfter", c.State(name));
        if (compile) c.Note("compile" + plc.Name, Describe.Safe(() => c.Compile(plc, plc.Name)));
        if (name == Names.Udt || name == Names.FbUdt)
            c.Note("udtUserMembersAfter", Describe.Safe(() => c.UserTypedMembers(c.Plc1, Names.UdtUser)));
        if (name == Names.Fb)
        {
            c.Note("fbDbInstanceOfName", Describe.Safe(() => (F.Block(c.Plc1, Names.FbDb) as InstanceDB)?.InstanceOfName));
            c.Note("compileCaller", Describe.Safe(() => c.Compile(F.Block(c.Plc1, Names.Caller), Names.Caller)));
        }
    }
}

internal static class Catalog
{
    private static Case C(string id, string question, Action<Ctx> setup, Action<Ctx> act, Action<Ctx> inspect = null, LibMode library = LibMode.None) =>
        new Case { Id = id, Question = question, Project = true, Library = library, Setup = setup, Act = act, Inspect = inspect };

    // Crash-risk: --all skips it, --case runs it only when named alone.
    private static Case H(string id, string question, string hazard, Action<Ctx> setup, Action<Ctx> act, Action<Ctx> inspect)
    {
        var k = C(id, question, setup, act, inspect);
        k.Hazard = hazard;
        return k;
    }

    private const UpdatePathsMode Throw = UpdatePathsMode.ThrowIfPathsConflict;
    private static readonly Version V1 = F.V(0, 0, 1), V2 = F.V(0, 0, 2), V3 = F.V(0, 0, 3), V4 = F.V(0, 0, 4);

    private static Action<Ctx> ExportPlc1(string name) => c => c.Docs = c.ExportXml(F.Obj(c.Plc1, name), name);
    private static Action<Ctx> Before(string name, Action<Ctx> act) => c => { c.Note("stateBefore", c.State(name)); act(c); };
    private static Action<Ctx> After(string name) => c => { c.At("inspect"); c.Note("stateAfter", c.State(name)); };
    private static Action<Ctx> MakeAndRelease(string name) => c => { c.MakeTypeInPlc2(name); c.Release(c.Working, V1); };
    private static Action<Ctx> Released(string name, Version number, string tag) => Before(name, c => c.Working.Release(Ctx.NoDeps, number, "probe", tag));

    private static object SyncState(Ctx c) => new
    {
        target = Describe.Safe(() => F.Named(c.Extra, Names.Udt)),
        sourceDefault = Describe.Safe(() => Describe.Ver(F.Default(F.Type(c.PL, Names.Udt))))
    };

    private const CreateOrReleaseDependenciesMode AutoDeps = CreateOrReleaseDependenciesMode.AutomaticallyCreateOrReleaseDependenciesIfRequired;

    private static object McInstances(Ctx c) => Describe.Safe(() => F.Type(c.PL, Names.Udt).Versions
        .Select(v => new { number = v.VersionNumber.ToString(), masterCopies = v.MasterCopiesContainingInstances.Select(m => m.Name).ToList() }).ToList());

    // Chain plus a project master copy of the PLC_2 LT_Udt instance (P7c, P7c2).
    private static void ChainWithMasterCopy(Ctx c)
    {
        c.Chain(Names.Udt);
        c.At("MasterCopies.Create(" + Names.Plc2 + " " + Names.Udt + ")");
        var mc = c.PL.MasterCopyFolder.MasterCopies.Create(F.Source(F.Obj(c.Plc2, Names.Udt)));
        c.Note("masterCopy", Describe.Safe(() => mc.Name));
        c.Note("masterCopiesContainingInstances", McInstances(c));
    }

    // Re-imports the PLC_2 LT_Fb instance with only its block title changed, through the worker's
    // plc_write update_block_logic XML call (P12, P12c).
    private static void ReimportTitle(Ctx c, string title)
    {
        var block = F.Block(c.Plc2, Names.Fb);
        var exported = Path.Combine(c.Step("PlcBlock.Export", () => c.ExportXml(block, Names.Fb)).FullName, Names.Fb + ".xml");
        var doc = XDocument.Load(exported, LoadOptions.PreserveWhitespace);
        var text = F.BlockTitleText(doc) ?? throw new ProbeSetupException("No block-level Title or Comment text in the export.");
        c.Note("titleBefore", text.Value);
        text.Value = title;
        // plc_read hands callers XML without DocumentInfo (BlockXmlSanitizer.RemoveDocumentInfo); mirror that.
        doc.Root?.Elements().Where(e => e.Name.LocalName == "DocumentInfo").Remove();
        var edited = Path.Combine(c.Out("edited"), Names.Fb + ".xml");
        Directory.CreateDirectory(c.Out("edited"));
        doc.Save(edited, SaveOptions.DisableFormatting);
        var group = block.Parent as PlcBlockGroup ?? throw new ProbeSetupException("Block parent is not a PlcBlockGroup.");
        // Same native call and options as the worker's plc_write update_block_logic XML route
        // (TiaMcpServer.OpennessWorker/Openness/Block/BlockImporter.cs, ImportDocuments):
        //     target.Group.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
        var imported = c.Step("PlcBlockComposition.Import(Override)", () => group.Blocks.Import(new FileInfo(edited), ImportOptions.Override));
        c.Note("imported", Describe.Safe(() => imported.Select(x => x.Name).ToList()));
        c.Note("linkAfterImport", Describe.Safe(() => Describe.Link(F.Block(c.Plc2, Names.Fb))));
        c.Note("inWorkGuid", Describe.Safe(() => c.Working.Guid.ToString()));
    }

    // Document (SIMATIC SD) route on the PLC_2 object: ExportAsDocuments, optional edit of the .s7dcl text,
    // ImportFromDocuments(Override) into the object's own group (P15b, P15c).
    private static void DocumentsRoundTrip(Ctx c, string name, Func<string, string> edit)
    {
        var obj = F.Obj(c.Plc2, name);
        var dir = new DirectoryInfo(c.Out("sd"));
        dir.Create();
        c.Note("sdExport", c.Step("ExportAsDocuments", () => obj is PlcType t ? (object)t.ExportAsDocuments(dir, name) : ((PlcBlock)obj).ExportAsDocuments(dir, name)));
        var files = dir.GetFiles();
        c.Note("sdFiles", files.Select(f => new { name = f.Name, bytes = f.Length }).ToList());
        var dcl = files.Where(f => f.Extension.Equals(".s7dcl", StringComparison.OrdinalIgnoreCase)).ToList();
        c.Note("sdDclText", dcl.Select(f => { var s = File.ReadAllText(f.FullName); return s.Length > 4000 ? s.Substring(0, 4000) : s; }).ToList());
        if (edit != null)
        {
            var applied = false;
            foreach (var f in dcl)
            {
                var bytes = File.ReadAllBytes(f.FullName);
                var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var text = File.ReadAllText(f.FullName);
                var edited = edit(text);
                if (edited == text) continue;
                File.WriteAllText(f.FullName, edited, new UTF8Encoding(bom));
                applied = true;
            }
            c.Note("sdEditApplied", applied);
        }
        var result = c.Step("ImportFromDocuments(Override)", () => obj is PlcType t
            ? (object)((PlcTypeGroup)t.Parent).Types.ImportFromDocuments(dir, name, ImportDocumentOptions.Override)
            : ((PlcBlockGroup)((PlcBlock)obj).Parent).Blocks.ImportFromDocuments(dir, name, ImportDocumentOptions.Override));
        c.Note("sdImport", result);
        c.Note("linkAfterImport", Describe.Safe(() => Describe.Link(F.Obj(c.Plc2, name))));
    }

    // Adds "probeAdded : Int;" after the "b : Int;" member line of LT_Udt's .s7dcl text, keeping indent and line ending.
    private static string AddDclMember(string text)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        return new Regex(@"^([ \t]*)b\s*:\s*Int\s*;[^\r\n]*$", RegexOptions.Multiline)
            .Replace(text, m => m.Value + newline + m.Groups[1].Value + "probeAdded : Int;", 1);
    }

    private static void Sync(Ctx c, string step)
    {
        c.Step("UpdateLibrary(" + step + ")", () => F.Type(c.PL, Names.Udt).UpdateLibrary(c.Extra, DeleteUnusedVersionsMode.DoNotDelete,
            StructureConflictResolutionMode.CancelIfStructureConflicts, ForceUpdateMode.SetOnlyHigherUpdatedVersionAsDefault));
        c.Note("sync-" + step, SyncState(c));
    }

    internal static readonly Case[] All =
    {
        C("P1u", "Convert LT_Udt in place: export, delete, CreateFromDocuments into PLC_1 in ExclusiveAccess + Transaction(project), commit; then compile PLC_1 (P2).",
            ExportPlc1(Names.Udt), Before(Names.Udt, c => Route.Convert(c, c.Plc1, Names.Udt, true, null)), c => Route.Inspect(c, c.Plc1, Names.Udt, true)),
        C("P1u-notx", "P1u without ExclusiveAccess/Transaction (comparison).",
            ExportPlc1(Names.Udt), Before(Names.Udt, c => Route.Convert(c, c.Plc1, Names.Udt, false, null)), c => Route.Inspect(c, c.Plc1, Names.Udt, true)),
        C("P1f", "Convert LT_Fb in place with the P1u route (BlockGroup); then compile PLC_1 and LT_Caller (P2).",
            ExportPlc1(Names.Fb), Before(Names.Fb, c => Route.Convert(c, c.Plc1, Names.Fb, true, null)), c => Route.Inspect(c, c.Plc1, Names.Fb, true)),
        C("P1r-after-import", "P1u route, deliberate failure after the import and link check: does disposing the uncommitted transaction roll back fully?",
            ExportPlc1(Names.Udt), Before(Names.Udt, c => Route.Convert(c, c.Plc1, Names.Udt, true, "import")), c => Route.Inspect(c, c.Plc1, Names.Udt, false)),
        C("P1r-after-delete", "P1u route, deliberate failure right after deleting the original: is LT_Udt restored?",
            ExportPlc1(Names.Udt), Before(Names.Udt, c => Route.Convert(c, c.Plc1, Names.Udt, true, "delete")), c => Route.Inspect(c, c.Plc1, Names.Udt, false)),
        C("P1n", "P1u route into the nested project-library type folder LT_Folder/LT_Sub (made in setup): is the type created there and linked?",
            c =>
            {
                ExportPlc1(Names.Udt)(c);
                c.At("TypeFolder.Folders.Create(LT_Folder/LT_Sub)");
                c.Folder = c.PL.TypeFolder.Folders.Create("LT_Folder").Folders.Create("LT_Sub");
            },
            Before(Names.Udt, c => Route.Convert(c, c.Plc1, Names.Udt, true, null, c.Folder)),
            c =>
            {
                c.Note("inNestedFolder", Describe.Safe(() => c.Folder.Types.Select(t => t.Name).ToList()));
                c.Note("inRootFolder", Describe.Safe(() => c.PL.TypeFolder.Types.Select(t => t.Name).ToList()));
                Route.Inspect(c, c.Plc1, Names.Udt, false);
            }),
        C("P3", "Convert LT_FbUdt (uses the plain UDT LT_Udt) with the P1u route; if created, release 0.0.1. Does a type for LT_Udt appear?",
            ExportPlc1(Names.FbUdt),
            Before(Names.FbUdt, c =>
            {
                Route.Convert(c, c.Plc1, Names.FbUdt, true, null);
                c.Note("udtTypeAfterCreate", F.Named(c.PL, Names.Udt));
                var inWork = c.Step("find-in-work", () => F.Type(c.PL, Names.FbUdt).Versions.FirstOrDefault(v => v.State == LibraryTypeVersionState.InWork));
                c.Note("versionsAfterCreate", F.Versions(F.Type(c.PL, Names.FbUdt)));
                if (inWork == null) return; // no in-work version: the note is the observation
                c.Step("Release(0.0.1)", () => inWork.Release(Ctx.NoDeps, V1, "probe", "P3"));
                c.Note("udtTypeAfterRelease", F.Named(c.PL, Names.Udt));
            }),
            c => { c.Note("udtTypeAfter", Describe.Safe(() => F.Named(c.PL, Names.Udt))); Route.Inspect(c, c.Plc1, Names.FbUdt, true); }),
        C("P3b", "P3 releasing with AutomaticallyCreateOrReleaseDependenciesIfRequired: does Portal make the plain LT_Udt a type and release it too?",
            ExportPlc1(Names.FbUdt),
            Before(Names.FbUdt, c =>
            {
                Route.Convert(c, c.Plc1, Names.FbUdt, true, null);
                var inWork = c.Step("find-in-work", () => F.InWork(F.Type(c.PL, Names.FbUdt)));
                c.Step("Release(0.0.1, automatic dependencies)", () => inWork.Release(AutoDeps, V1, "probe", "P3b"));
                c.Note("versionsAfterRelease", Describe.Safe(() => F.Versions(F.Type(c.PL, Names.FbUdt))));
                c.Note("udtTypeAfterRelease", Describe.Safe(() => F.Named(c.PL, Names.Udt)));
            }),
            c =>
            {
                c.Note("udtPlc1Link", Describe.Safe(() => Describe.Link(F.Obj(c.Plc1, Names.Udt))));
                Route.Inspect(c, c.Plc1, Names.FbUdt, true);
            }),
        C("P4", "Source already a type instance: export the PLC_2 LT_Udt instance (released 0.0.1), delete it and CreateFromDocuments again into PLC_2 in one transaction. Second type, refusal or relink?",
            c => { MakeAndRelease(Names.Udt)(c); c.Docs = c.ExportXml(F.Obj(c.Plc2, Names.Udt), Names.Udt); },
            Before(Names.Udt, c => Route.Convert(c, c.Plc2, Names.Udt, true, null)), After(Names.Udt)),
        C("P4c", "P4 with PLC_2 compiled before the export (run 1's P4 export was refused as inconsistent): second type, refusal or relink?",
            c => { MakeAndRelease(Names.Udt)(c); c.CompileFirst(c.Plc2, Names.Udt, "setup"); c.Docs = c.ExportXml(F.Obj(c.Plc2, Names.Udt), Names.Udt); },
            Before(Names.Udt, c => Route.Convert(c, c.Plc2, Names.Udt, true, null)), After(Names.Udt)),
        C("P5", "Version GUID and number: in-work after create (P13a), across Release 0.0.1, Edit and Release 0.0.2.",
            c => c.MakeTypeInPlc2(Names.Udt),
            Before(Names.Udt, c =>
            {
                var type = c.Made;
                var w = c.Working;
                var createdGuid = w.Guid;
                c.Note("inWorkAfterCreate", Describe.Ver(w));
                c.Step("Release(0.0.1)", () => w.Release(Ctx.NoDeps, V1, "probe", "P5"));
                c.Note("afterRelease001", new
                {
                    sameObject = Describe.Safe(() => Describe.Ver(w)),
                    refound = Describe.Safe(() => Describe.Ver(F.Find(type, V1))),
                    sameObjectGuidEqualsRefound = Describe.Safe(() => w.Guid == F.Find(type, V1).Guid),
                    createdGuidEqualsRefound = Describe.Safe(() => createdGuid == F.Find(type, V1).Guid),
                    versions = Describe.Safe(() => F.Versions(type))
                });
                var w2 = c.Step("Edit via PLC_2 instance", () => c.EditVia(Names.Udt));
                var editGuid = w2.Guid;
                c.Note("inWorkAfterEdit", Describe.Ver(w2));
                c.Step("Release(0.0.2)", () => w2.Release(Ctx.NoDeps, V2, "probe", "P5"));
                c.Note("afterRelease002", new
                {
                    sameObject = Describe.Safe(() => Describe.Ver(w2)),
                    refound = Describe.Safe(() => Describe.Ver(F.Find(type, V2))),
                    sameObjectGuidEqualsRefound = Describe.Safe(() => w2.Guid == F.Find(type, V2).Guid),
                    editGuidEqualsRefound = Describe.Safe(() => editGuid == F.Find(type, V2).Guid),
                    versions = Describe.Safe(() => F.Versions(type))
                });
            }), After(Names.Udt)),
        C("P6", "SetAsDefault on the older released version 0.0.1 (0.0.2 is default and linked).",
            c => c.Chain(Names.Udt), Before(Names.Udt, c => F.Find(F.Type(c.PL, Names.Udt), V1).SetAsDefault()), After(Names.Udt)),
        C("P7a", "LibraryType.Delete on a type with a PLC_2 instance and no dependents.",
            c => c.Chain(Names.Udt), Before(Names.Udt, c => F.Type(c.PL, Names.Udt).Delete()), After(Names.Udt)),
        C("P7b", "Delete version 0.0.2 (default, linked by the PLC_2 instance).",
            c => c.Chain(Names.Udt), Before(Names.Udt, c => F.Find(F.Type(c.PL, Names.Udt), V2).Delete()), After(Names.Udt)),
        C("P7c", "LibraryType.Delete while a project master copy holds a PLC_2 instance.",
            ChainWithMasterCopy, Before(Names.Udt, c => F.Type(c.PL, Names.Udt).Delete()), After(Names.Udt)),
        C("P7c2", "LibraryType.Delete when only a project master copy holds an instance (the PLC_2 instance is deleted in setup).",
            c =>
            {
                ChainWithMasterCopy(c);
                c.Step("Delete PLC_2 instance", () => F.Delete(F.Obj(c.Plc2, Names.Udt)));
                c.Note("masterCopiesContainingInstancesAfterInstanceDelete", McInstances(c));
            },
            Before(Names.Udt, c => F.Type(c.PL, Names.Udt).Delete()), After(Names.Udt)),
        C("P7d", "Delete the non-default released version 0.0.2 while the PLC_2 instance links to it (0.0.1 set as default in setup).",
            c => { c.Chain(Names.Udt); c.Step("SetAsDefault(0.0.1)", () => F.Find(F.Type(c.PL, Names.Udt), V1).SetAsDefault()); },
            Before(Names.Udt, c => F.Find(F.Type(c.PL, Names.Udt), V2).Delete()), After(Names.Udt)),
        C("P8", "Instantiate a global type version the project library lacks into PLC_2 (setup: LT_Udt type made in the project, released 0.0.1, synchronized into the global copy, then its PLC_2 instance and project type deleted).",
            c =>
            {
                MakeAndRelease(Names.Udt)(c);
                c.Step("UpdateLibrary(project -> global)", () => F.Type(c.PL, Names.Udt).UpdateLibrary(c.Lib, DeleteUnusedVersionsMode.DoNotDelete,
                    StructureConflictResolutionMode.CancelIfStructureConflicts, ForceUpdateMode.SetOnlyHigherUpdatedVersionAsDefault));
                c.Step("Delete PLC_2 instance", () => ((PlcType)F.Obj(c.Plc2, Names.Udt)).Delete());
                c.Step("Delete project type", () => F.Type(c.PL, Names.Udt).Delete());
                c.Note("setupState", new
                {
                    global = Describe.Safe(() => F.Versions(F.Type(c.Lib, Names.Udt))),
                    projectHasType = Describe.Safe(() => F.Types(c.PL.TypeFolder).Any(t => t.Name == Names.Udt)),
                    plc2HasUdt = Describe.Safe(() => F.TryObj(c.Plc2, Names.Udt) != null)
                });
            },
            Before(Names.Udt, c =>
            {
                var gv = F.Default(F.Type(c.Lib, Names.Udt));
                c.Note("globalDefault", Describe.Ver(gv));
                c.Note("returned", c.Plc2.TypeGroup.Types.CreateFrom(F.As<PlcTypeLibraryTypeVersion>(gv), Throw));
            }),
            After(Names.Udt), LibMode.ReadWrite),
        C("P9", "UpdateLibrary into a fresh library with SetOnlyHigherUpdatedVersionAsDefault: a) initial, b) source default lowered to 0.0.1 after 0.0.3, c) higher 0.0.4 default.",
            c =>
            {
                c.Chain(Names.Udt);
                c.At("GlobalLibraries.Create(" + Names.Fresh + ")");
                c.Extra = c.Portal.GlobalLibraries.Create<UserGlobalLibrary>(new DirectoryInfo(c.OutDir), Names.Fresh);
            },
            c =>
            {
                Sync(c, "a");
                c.Release(c.Step("Edit via PLC_2 instance", () => c.EditVia(Names.Udt)), V3);
                c.Step("SetAsDefault(0.0.1)", () => F.Find(F.Type(c.PL, Names.Udt), V1).SetAsDefault());
                Sync(c, "b");
                c.Release(c.Step("Edit via PLC_2 instance", () => c.EditVia(Names.Udt)), V4);
                c.Note("sourceBeforeSyncC", F.Versions(F.Type(c.PL, Names.Udt)));
                Sync(c, "c");
            }, After(Names.Udt)),
        C("P10", "Export the know-how protected LT_FbKhp (fixtureMissing if absent).",
            null,
            Before(Names.FbKhp, c =>
            {
                if (!(F.TryObj(c.Plc1, Names.FbKhp) is PlcBlock b)) { c.Note("fixtureMissing", true); return; }
                c.Note("isKnowHowProtected", Describe.Safe(() => b.IsKnowHowProtected));
                c.Step("PlcBlock.Export", () => c.ExportXml(b, Names.FbKhp));
            }), After(Names.FbKhp)),
        C("P11", "Discard the in-work version (released 0.0.1 exists): what happens to the PLC_2 test instance?",
            c => { MakeAndRelease(Names.Udt)(c); c.EditVia(Names.Udt); },
            Before(Names.Udt, c => c.Working.Discard()), After(Names.Udt)),
        C("P11c", "Discard after a real content change: the in-work PLC_2 LT_Udt instance gets a member through the update_type_content XML route, then Discard. Does the instance return to the released 0.0.1 content?",
            c =>
            {
                MakeAndRelease(Names.Udt)(c);
                c.CompileFirst(c.Plc2, Names.Udt, "released");
                c.Note("releasedContent", c.Content(c.Plc2, Names.Udt, "released"));
                c.EditVia(Names.Udt);
                c.CompileFirst(c.Plc2, Names.Udt, "inWork");
                c.ImportWithExtraMember(c.Plc2, Names.Udt);
                c.Note("linkAfterChange", Describe.Safe(() => Describe.Link(F.Obj(c.Plc2, Names.Udt))));
                c.CompileFirst(c.Plc2, Names.Udt, "changed");
                c.Note("changedContent", c.Content(c.Plc2, Names.Udt, "changed"));
            },
            Before(Names.Udt, c => c.Step("Discard", () => F.InWork(F.Type(c.PL, Names.Udt)).Discard())),
            c =>
            {
                After(Names.Udt)(c);
                c.CompileFirst(c.Plc2, Names.Udt, "afterDiscard");
                c.Note("contentAfterDiscard", Describe.Safe(() => c.Content(c.Plc2, Names.Udt, "afterDiscard")));
            }),
        C("P12", "Re-import an in-work FB test instance through the update_block_logic XML route with only its title changed: still linked to the in-work version?",
            c => { MakeAndRelease(Names.Fb)(c); c.EditVia(Names.Fb); },
            Before(Names.Fb, c => ReimportTitle(c, "probe P12 title edit")), After(Names.Fb)),
        C("P12c", "P12 with the in-work PLC_2 test instance compiled before the export; then release the in-work version as 0.0.2. Still linked after the import, and does the release keep the edited title?",
            c => { MakeAndRelease(Names.Fb)(c); c.EditVia(Names.Fb); c.CompileFirst(c.Plc2, Names.Fb, "inWork"); },
            Before(Names.Fb, c =>
            {
                ReimportTitle(c, "probe P12c title edit");
                c.CompileFirst(c.Plc2, Names.Fb, "afterImport");
                var inWork = c.Step("find-in-work", () => F.InWork(F.Type(c.PL, Names.Fb)));
                c.Note("inWorkBeforeRelease", Describe.Ver(inWork));
                c.Step("Release(0.0.2)", () => inWork.Release(Ctx.NoDeps, V2, "probe", "P12c"));
                c.Note("versionsAfterRelease", Describe.Safe(() => F.Versions(F.Type(c.PL, Names.Fb))));
                c.Note("linkAfterRelease", Describe.Safe(() => Describe.Link(F.Block(c.Plc2, Names.Fb))));
                c.CompileFirst(c.Plc2, Names.Fb, "afterRelease");
                c.Note("titleAfterRelease", Describe.Safe(() => F.BlockTitleText(XDocument.Load(c.ExportFile(c.Plc2, Names.Fb, "afterRelease")))?.Value));
            }), After(Names.Fb)),
        C("P15a", "Delete the compiled in-work PLC_2 LT_Udt test instance directly: refused (and the 'Library test environment cannot be closed' dialog) like the Override imports in P11c/P12c?",
            c => { MakeAndRelease(Names.Udt)(c); c.EditVia(Names.Udt); c.CompileFirst(c.Plc2, Names.Udt, "inWork"); },
            Before(Names.Udt, c => c.Step("Delete test instance", () => F.Delete(F.Obj(c.Plc2, Names.Udt)))), After(Names.Udt)),
        C("P15b", "Document route on the compiled in-work PLC_2 LT_Fb test instance: ExportAsDocuments, then ImportFromDocuments(Override) unchanged. Accepted in place, still linked to the in-work version?",
            c => { MakeAndRelease(Names.Fb)(c); c.EditVia(Names.Fb); c.CompileFirst(c.Plc2, Names.Fb, "inWork"); },
            Before(Names.Fb, c => DocumentsRoundTrip(c, Names.Fb, null)),
            c => { After(Names.Fb)(c); c.CompileFirst(c.Plc2, Names.Fb, "afterImport"); }),
        C("P15c", "Document route with a content change on the compiled in-work PLC_2 LT_Udt test instance: add a member in the .s7dcl, ImportFromDocuments(Override), compile, release 0.0.2. Does the released version carry the member?",
            c => { MakeAndRelease(Names.Udt)(c); c.EditVia(Names.Udt); c.CompileFirst(c.Plc2, Names.Udt, "inWork"); },
            Before(Names.Udt, c =>
            {
                DocumentsRoundTrip(c, Names.Udt, AddDclMember);
                c.CompileFirst(c.Plc2, Names.Udt, "afterImport");
                c.Note("contentAfterImport", Describe.Safe(() => c.Content(c.Plc2, Names.Udt, "afterImport")));
                var inWork = c.Step("find-in-work", () => F.InWork(F.Type(c.PL, Names.Udt)));
                c.Step("Release(0.0.2)", () => inWork.Release(Ctx.NoDeps, V2, "probe", "P15c"));
                c.Note("versionsAfterRelease", Describe.Safe(() => F.Versions(F.Type(c.PL, Names.Udt))));
                c.Note("linkAfterRelease", Describe.Safe(() => Describe.Link(F.Obj(c.Plc2, Names.Udt))));
                c.CompileFirst(c.Plc2, Names.Udt, "afterRelease");
                c.Note("contentAfterRelease", Describe.Safe(() => c.Content(c.Plc2, Names.Udt, "afterRelease")));
            }), After(Names.Udt)),
        C("P15d", "Override import (update_type_content XML route) with an added member onto a compiled RELEASED PLC_2 LT_Udt instance (no edit): accepted? Still a type instance, of which version, and does an in-work version appear?",
            c => { MakeAndRelease(Names.Udt)(c); c.CompileFirst(c.Plc2, Names.Udt, "released"); c.Note("releasedContent", c.Content(c.Plc2, Names.Udt, "released")); },
            Before(Names.Udt, c => c.ImportWithExtraMember(c.Plc2, Names.Udt)),
            c =>
            {
                After(Names.Udt)(c);
                c.Note("linkAfterImport", Describe.Safe(() => Describe.Link(F.Obj(c.Plc2, Names.Udt))));
                c.CompileFirst(c.Plc2, Names.Udt, "afterImport");
                c.Note("contentAfterImport", Describe.Safe(() => c.Content(c.Plc2, Names.Udt, "afterImport")));
            }),
        H("P13b", "Release the in-work version with a null version number (and null author/comment).",
            "Version validation may run in Portal itself (see T7Z, which killed Portal).",
            c => c.MakeTypeInPlc2(Names.Udt), Before(Names.Udt, c => c.Working.Release(Ctx.NoDeps, null, null, null)), After(Names.Udt)),
        H("P14a", "Release the first version as 1.0.0.", "CheckVersionValuesAreWithinRange killed Portal for 0.0.0 (T7Z).",
            c => c.MakeTypeInPlc2(Names.Udt), Released(Names.Udt, F.V(1, 0, 0), "P14a"), After(Names.Udt)),
        H("P14b", "Release the first version as 0.1.0.", "CheckVersionValuesAreWithinRange killed Portal for 0.0.0 (T7Z).",
            c => c.MakeTypeInPlc2(Names.Udt), Released(Names.Udt, F.V(0, 1, 0), "P14b"), After(Names.Udt)),
        C("P14c", "Release the first version as 0.0.1 (control; known valid).",
            c => c.MakeTypeInPlc2(Names.Udt), Released(Names.Udt, V1, "P14c"), After(Names.Udt)),
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
        if (o.GetType().Name.Contains("AnonymousType") || o is Dictionary<string, object>) return o; // probe-built evidence, already plain
        var d = new Dictionary<string, object> { { "clrType", o.GetType().FullName } };
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0 || p.Name == "Parent") continue;
            d[p.Name] = Safe(() => Value(p.GetValue(o), depth + 1));
        }
        return d;
    }

    internal static object Ver(LibraryTypeVersion v) => new
    {
        number = Safe(() => v.VersionNumber.ToString()), guid = Safe(() => v.Guid.ToString()),
        state = Safe(() => v.State.ToString()), isDefault = Safe(() => v.IsDefault)
    };

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
                isDefault = Safe(() => v.IsDefault), dependencies = Safe(() => v.Dependencies.Select(x => x.Guid.ToString()).ToList()),
                masterCopiesContainingInstances = Safe(() => v.MasterCopiesContainingInstances.Select(m => m.Name).ToList())
            }).ToList())
        }).ToList()),
        folders = Safe(() => f.Folders.Select(TypeFolder).ToList())
    };

    internal static object Plc(PlcSoftware p) => p == null ? null : new { name = Safe(() => p.Name), blocks = Safe(() => Blocks(p.BlockGroup, "")), types = Safe(() => PlcTypes(p.TypeGroup, "")) };

    private static List<object> Blocks(PlcBlockGroup g, string path) =>
        g.Blocks.Select(b => (object)new { path, name = Safe(() => b.Name), link = Safe(() => Link(b)), instanceOfName = b is InstanceDB i ? Safe(() => i.InstanceOfName) : null })
            .Concat(g.Groups.SelectMany(s => Blocks(s, path + "/" + s.Name))).ToList();

    private static List<object> PlcTypes(PlcTypeGroup g, string path) =>
        g.Types.Select(t => (object)new { path, name = Safe(() => t.Name), link = Safe(() => Link(t)) })
            .Concat(g.Groups.SelectMany(s => PlcTypes(s, path + "/" + s.Name))).ToList();

    internal static object Link(IEngineeringServiceProvider o)
    {
        var info = o.GetService<LibraryTypeInstanceInfo>();
        if (info == null) return null;
        var v = info.LibraryTypeVersion;
        return new { type = Safe(() => v.TypeObject.Name), typeGuid = Safe(() => v.TypeObject.Guid.ToString()), number = Safe(() => v.VersionNumber.ToString()),
            version = Safe(() => v.Guid.ToString()), state = Safe(() => v.State.ToString()) };
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
    private volatile bool portalLost;     // fatal exception or the attached handle's Disposed event
    private bool idle = true;             // false once a case or cleanup may have left Portal busy
    internal Runner(Options options) { this.options = options; }

    internal int Run()
    {
        Program.StartRequest("portal-processes");
        var process = TiaPortal.GetProcesses().SingleOrDefault(p => p.Id == options.Pid)
            ?? throw new InvalidOperationException("Exact Portal PID not found; no Portal was started.");
        // Never Dispose the TiaPortalProcess (it closed every Portal instance once); only the attached handle is disposed.
        Program.Progress("attach");
        using (portal = process.Attach())
        {
            if (portal.GetCurrentProcess().Id != options.Pid) throw new InvalidOperationException("Attach returned a different Portal PID.");
            portal.Confirmation += OnConfirmation;
            portal.Disposed += (s, e) => portalLost = true;
            try
            {
                // The pristine project and library may be open in this Portal. They are closed (only when saved),
                // the cases run on copies, and both are reopened afterwards as they were found.
                Program.Progress("originals-check");
                if (portal.Projects.Count > 1) throw new InvalidOperationException("Portal has more than one project open. Nothing was changed.");
                var openProject = portal.Projects.SingleOrDefault();
                if (openProject != null && !SamePath(openProject.Path.FullName, options.ProjectFile))
                    throw new InvalidOperationException("Portal has a different project open; close it first. Nothing was changed.");
                if (openProject != null && openProject.IsModified)
                    throw new InvalidOperationException("The disposable project has unsaved changes; save it first. Nothing was changed.");
                var openLibs = portal.GlobalLibraries.OfType<UserGlobalLibrary>().Where(l => Program.Under(l.Path.FullName, options.Root)).ToList();
                if (openLibs.Any(l => !SamePath(l.Path.FullName, options.LibraryFile)))
                    throw new InvalidOperationException("Another library below --disposable-root is open; close it first. Nothing was changed.");
                var openLib = openLibs.SingleOrDefault();
                if (openLib != null && openLib.IsModified)
                    throw new InvalidOperationException("The disposable library has unsaved changes; save it first. Nothing was changed.");
                var reopenProject = openProject != null;
                var reopenLibraryReadOnly = openLib?.IsReadOnly;
                Program.Emit(new { type = "attached", portalPid = options.Pid, assembly = typeof(TiaPortal).Assembly.FullName, step7 = typeof(PlcSoftware).Assembly.FullName,
                    originals = new { projectWasOpen = reopenProject, libraryWasOpen = openLib != null, libraryReadOnly = reopenLibraryReadOnly } });
                if (openLib != null) { Program.Progress("originals:close-library"); openLib.Close(); }
                if (openProject != null) { Program.Progress("originals:close-project"); openProject.Close(); }
                try
                {
                    if (options.CheckFixture) return new FixtureCheck(this).Run();
                    foreach (var id in options.Cases)
                    {
                        if (portalLost) { Program.Emit(new { type = "stopped", reason = "Portal attachment was disposed; no later case attempted." }); return 2; }
                        if (!RunCase(Catalog.All.Single(c => c.Id == id))) return 2;
                    }
                    Program.Emit(new { type = "complete", cases = options.Cases.Length });
                    return 0;
                }
                catch (Exception ex) when (Program.IsFatal(ex)) { portalLost = true; throw; }
                finally
                {
                    if (portalLost) Program.Emit(new { type = "originals-not-reopened", reason = "Portal loss; reopen the originals by hand." });
                    else if (!idle) Program.Emit(new { type = "originals-not-reopened", reason = "Portal may not be idle; close the case copy without saving and reopen the originals by hand." });
                    else ReopenOriginals(reopenProject, reopenLibraryReadOnly);
                }
            }
            finally { portal.Confirmation -= OnConfirmation; }
        }
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void ReopenOriginals(bool project, bool? libraryReadOnly)
    {
        Program.StartRequest("originals:reopen");
        try
        {
            if (project) { Program.Progress("originals:reopen-project"); portal.Projects.Open(new FileInfo(options.ProjectFile)); }
            if (libraryReadOnly.HasValue)
            {
                Program.Progress("originals:reopen-library");
                portal.GlobalLibraries.Open(new FileInfo(options.LibraryFile), libraryReadOnly.Value ? OpenMode.ReadOnly : OpenMode.ReadWrite);
            }
            Program.Emit(new { type = "originals-reopened", project, library = libraryReadOnly.HasValue });
        }
        catch (Exception ex) { Program.Emit(new { type = "originals-reopen-failed", phase = Program.CurrentPhase, error = Program.Error(ex) }); }
    }

    // Records every Portal confirmation and answers the most conservative available choice.
    private void OnConfirmation(object sender, ConfirmationEventArgs e)
    {
        var result = e.Choices.HasFlag(ConfirmationChoices.No) ? ConfirmationResult.No
            : e.Choices.HasFlag(ConfirmationChoices.Cancel) ? ConfirmationResult.Cancel
            : e.Choices.HasFlag(ConfirmationChoices.Ok) ? ConfirmationResult.Ok : (ConfirmationResult?)null;
        confirmations.Add(new { phase = Program.CurrentPhase, caption = e.Caption, text = e.Text, detail = e.DetailText, choices = e.Choices.ToString(),
            answered = result?.ToString() ?? "unset (no No, Cancel or Ok offered)" });
        if (result.HasValue) e.Result = result.Value;
    }

    internal Ctx NewCtx(string id)
    {
        var c = new Ctx { Portal = portal, CaseDir = Path.Combine(options.Work, id), LibName = options.LibName };
        c.OutDir = Path.Combine(c.CaseDir, "out");
        c.LibDir = Path.Combine(c.CaseDir, "lib", c.LibName);
        c.LibFile = Path.Combine(c.LibDir, c.LibName + ".al21");
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
        if (k.Hazard != null) rec["crashRisk"] = k.Hazard;
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
                    rec["isDeliberateRollback"] = ex is DeliberateRollbackException;
                    fatal = Program.IsFatal(ex);
                }
                if (k.Inspect != null && !fatal && !c.Held && !portalLost)
                {
                    try { k.Inspect(c); }
                    catch (Exception ex) { rec["inspectError"] = Program.Error(ex); rec["inspectStage"] = c.Stage; fatal = Program.IsFatal(ex); }
                }
            }
            if (!fatal) rec["after"] = Snapshot(c);
            rec["notes"] = c.Notes;
        }
        rec["confirmationsDuringCase"] = confirmations.ToList();
        if (fatal || portalLost) { portalLost = true; rec["stopped"] = "Portal loss; no cleanup or retry attempted."; Program.Emit(rec); return false; }
        if (c.Held)
        {
            idle = false;
            rec["stopped"] = "transaction/exclusive access may still be held; detach only (no inspect, cleanup or originals reopen).";
            Program.Emit(rec);
            return false;
        }

        var cleanup = Cleanup(c, out bool clean);
        rec["cleanup"] = cleanup;
        if (clean && File.Exists(c.LibFile)) rec["persisted"] = Persisted(c, ref clean);
        if (!clean) idle = false;
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
        Program.StartRequest(Path.GetFileName(c.CaseDir) + ".cleanup");
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
        if (!clean) idle = false;
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

    internal static PlcSoftware FindPlc(Project project, string name) => (PlcSoftware)FindPlcItem(project, name).GetService<SoftwareContainer>().Software;

    internal static DeviceItem FindPlcItem(Project project, string name)
    {
        var matches = AllDevices(project).SelectMany(d => AllItems(d.DeviceItems))
            .Where(i => i.GetService<SoftwareContainer>()?.Software is PlcSoftware p && p.Name == name).ToList();
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

// Check on a copy that the fixture matches every row the cases assume. Compiles PLC_1 on the copy; never saves.
internal sealed class FixtureCheck
{
    private readonly Runner runner;
    private readonly List<object> checks = new List<object>();
    private bool pass = true;
    internal FixtureCheck(Runner runner) { this.runner = runner; }

    // test returns null on pass, otherwise the reason.
    private void Check(string what, Func<string> test)
    {
        try { var reason = test(); pass &= reason == null; checks.Add(new { what, pass = reason == null, reason }); }
        catch (Exception ex) when (!Program.IsFatal(ex)) { pass = false; checks.Add(new { what, pass = false, reason = ex.GetType().Name + ": " + ex.Message }); }
    }

    private void Info(string what, Func<object> read) => checks.Add(new { what, info = Describe.Safe(read) });

    private static string Linked(IEngineeringServiceProvider o) => o.GetService<LibraryTypeInstanceInfo>() == null ? null : "is a library type instance";

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
        string Xml(PlcSoftware plc, string name) => Path.Combine(c.ExportXml(F.Obj(plc, name), name, "fixture").FullName, name + ".xml");
        string Fb(string name)
        {
            var b = c.Plc1.BlockGroup.Blocks.Find(name);
            if (b == null) return "not in the PLC_1 blocks root";
            if (!(b is FB)) return "is " + b.GetType().Name + ", not an FB";
            return Linked(b);
        }

        Check("global library name matches its file", () => gl.Name == c.LibName ? null : "library name differs from its file name");
        // P8 opens the library copy ReadWrite; File.Copy keeps the read-only attribute.
        Check("global library is writable", () =>
            Directory.GetFiles(c.LibDir, "*", SearchOption.AllDirectories).Any(f => (File.GetAttributes(f) & FileAttributes.ReadOnly) != 0) ? "a library file is read-only"
            : gl.IsWriteProtected ? "library is write-protected" : null);
        Info("PLC_1/PLC_2 device item type identifiers (expected S7-1500)", () => new[] { Names.Plc1, Names.Plc2 }
            .Select(n => Runner.FindPlcItem(c.Project, n).GetAttribute("TypeIdentifier")?.ToString()).ToList());
        Check("PLC_2 holds no LT_* block or type", () =>
        {
            var found = F.BlockNames(c.Plc2.BlockGroup).Concat(F.TypeNames(c.Plc2.TypeGroup)).Where(n => n.StartsWith(Names.Prefix, StringComparison.Ordinal)).ToList();
            return found.Count == 0 ? null : "found " + string.Join(", ", found);
        });
        Check(Names.Udt + " in the PLC_1 types root, not a type instance", () =>
        {
            var t = c.Plc1.TypeGroup.Types.Find(Names.Udt);
            return t == null ? "not in the PLC_1 types root" : Linked(t);
        });
        Check(Names.UdtUser + " is a global DB in PLC_1 with a member of type \"" + Names.Udt + "\"", () =>
        {
            if (!(F.Block(c.Plc1, Names.UdtUser) is GlobalDB)) return "not a GlobalDB";
            return F.HasMember(Xml(c.Plc1, Names.UdtUser), "\"" + Names.Udt + "\"") ? null : "no member of type \"" + Names.Udt + "\"";
        });
        Check(Names.Fb + " is a plain FB in the PLC_1 blocks root that uses no UDT and has a block title or comment", () =>
        {
            var reason = Fb(Names.Fb);
            if (reason != null) return reason;
            var xml = Xml(c.Plc1, Names.Fb);
            if (F.AnyUserType(xml)) return "has a member of a user data type";
            return F.BlockTitleText(XDocument.Load(xml)) == null ? "no block-level Title/Comment text (P12 edits it)" : null;
        });
        Check(Names.FbDb + " is the instance DB of " + Names.Fb, () =>
            F.Block(c.Plc1, Names.FbDb) is InstanceDB i ? (i.InstanceOfName == Names.Fb ? null : "instance of " + i.InstanceOfName) : "not an InstanceDB");
        Check(Names.Caller + " is an FC that calls " + Names.Fb + " with " + Names.FbDb, () =>
        {
            if (!(F.Block(c.Plc1, Names.Caller) is FC)) return "not an FC";
            return F.Calls(Xml(c.Plc1, Names.Caller), Names.Fb, Names.FbDb) ? null : "no CallInfo Name=" + Names.Fb + " with instance " + Names.FbDb + " in its XML";
        });
        Check(Names.FbUdt + " is a plain FB in the PLC_1 blocks root with a static or input member of type \"" + Names.Udt + "\"", () =>
            Fb(Names.FbUdt) ?? (F.HasMember(Xml(c.Plc1, Names.FbUdt), "\"" + Names.Udt + "\"", "Static", "Input") ? null : "no Static/Input member of type \"" + Names.Udt + "\""));
        Info(Names.FbKhp + " (optional; P10 records fixtureMissing if absent)", () =>
            F.TryObj(c.Plc1, Names.FbKhp) is PlcBlock b ? (object)new { present = true, isKnowHowProtected = b.IsKnowHowProtected } : new { present = false });
        Check("global library has no LT_* type (P8 builds its own)", () =>
        {
            var found = F.Types(gl.TypeFolder).Select(t => t.Name).Where(n => n.StartsWith(Names.Prefix, StringComparison.Ordinal)).ToList();
            return found.Count == 0 ? null : "found " + string.Join(", ", found);
        });
        Check("project library has no LT_* type or master copy", () =>
        {
            var found = F.Types(c.PL.TypeFolder).Select(t => t.Name).Concat(F.MasterCopies(c.PL.MasterCopyFolder).Select(m => m.Name))
                .Where(n => n.StartsWith(Names.Prefix, StringComparison.Ordinal)).ToList();
            return found.Count == 0 ? null : "found " + string.Join(", ", found);
        });
        object baseline = null;
        // Unrelated compile errors are the baseline the cases compare against; only LT_* errors fail the fixture.
        Check("PLC_1 compiles with no errors under an LT_* object (other errors are the recorded baseline)", () =>
        {
            baseline = c.Compile(c.Plc1, Names.Plc1);
            var ltErrors = Convert.ToInt32(baseline.GetType().GetProperty("ltErrors").GetValue(baseline));
            return ltErrors == 0 ? null : ltErrors + " compile errors under LT_* objects";
        });

        var cleanup = runner.Cleanup(c, out bool clean);
        Program.Emit(new { type = "fixture-check", pass, checks, baselineCompile = baseline, cleanup, clean });
        return pass && clean ? 0 : 3;
    }
}

internal static class SelfTest
{
    private static readonly string[] ExpectedIds =
        { "P1u", "P1u-notx", "P1f", "P1r-after-import", "P1r-after-delete", "P1n", "P3", "P3b", "P4", "P4c", "P5", "P6", "P7a", "P7b", "P7c", "P7c2", "P7d",
          "P8", "P9", "P10", "P11", "P11c", "P12", "P12c", "P15a", "P15b", "P15c", "P15d", "P13b", "P14a", "P14b", "P14c" };
    private static readonly string[] CrashRisk = { "P13b", "P14a", "P14b" };

    private static void Assert(bool ok, string what) { if (!ok) throw new Exception("Self-test failed: " + what); }

    internal static int Run()
    {
        var ids = Catalog.All.Select(c => c.Id).ToList();
        Assert(ids.SequenceEqual(ExpectedIds), "case table is " + string.Join(",", ids));
        Assert(Catalog.All.All(c => c.Act != null && !string.IsNullOrWhiteSpace(c.Question) && c.Project), "every case has an act, a question and a project");
        Assert(Catalog.All.Where(c => c.Hazard != null).Select(c => c.Id).SequenceEqual(CrashRisk), "crash-risk set is P13b, P14a, P14b");

        var root = Path.Combine(Path.GetTempPath(), "ltp-selftest-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "proj", "Fixture.ap21");
        var library = Path.Combine(root, "ProbeLib", "ProbeLib.al21");
        var shortLibrary = Path.Combine(root, "Abc", "Abc.al21");
        var gitRoot = Path.Combine(root, "repo");
        foreach (var f in new[] { project, library, shortLibrary })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllText(f, "");
        }
        Directory.CreateDirectory(Path.Combine(gitRoot, ".git"));
        try
        {
            string[] Args(params string[] extra) =>
                new[] { "--pid", "1", "--disposable-root", root, "--project", project, "--library", library, "--work", Path.Combine(root, "work") }.Concat(extra).ToArray();
            var all = Options.Parse(Args("--confirm-disposable", "--all"));
            Assert(all.Cases.SequenceEqual(ExpectedIds.Except(CrashRisk)), "--all is every case except P13b, P14a, P14b, in table order");
            Assert(Options.Parse(Args("--confirm-disposable", "--case", "P1u,P9")).Cases.SequenceEqual(new[] { "P1u", "P9" }), "--case keeps the given order");
            foreach (var id in CrashRisk) Assert(Options.Parse(Args("--confirm-disposable", "--case", id)).Cases.SequenceEqual(new[] { id }), id + " runs alone");
            Options.Parse(Args("--confirm-disposable", "--check-fixture"));

            Program.InitRedactions(all);
            var sample = Program.Json.Serialize(new { libraries = "kept", path = library, msg = "project fixture holds library PROBELIB" });
            var scrubbed = Program.Scrub(sample);
            Assert(scrubbed == "{\"libraries\":\"kept\",\"path\":\"<root>\\\\<lib>\\\\<lib>.al21\",\"msg\":\"project <proj> holds library <lib>\"}", "redaction gave " + scrubbed);
            Assert(Program.InGitTree(Path.Combine(gitRoot, "inner")) && !Program.InGitTree(root), "git work tree detection");

            var rejections = new[]
            {
                Args("--confirm-disposable", "--case", "P1u,P1u"),
                Args("--confirm-disposable", "--all", "--library", shortLibrary),
                Args("--all"),
                Args("--confirm-disposable"),
                Args("--confirm-disposable", "--all", "--check-fixture"),
                Args("--confirm-disposable", "--case", "X99"),
                Args("--confirm-disposable", "--case", "P13b,P14a"),
                Args("--confirm-disposable", "--case", "P1u,P14b"),
                Args("--confirm-disposable", "--all", "--output", Path.Combine(root, "elsewhere.jsonl")),
                Args("--confirm-disposable", "--all", "--work", Path.GetTempPath()),
                Args("--confirm-disposable", "--all", "--work", Path.Combine(root, "proj", "work")),
                Args("--confirm-disposable", "--all", "--work", Path.Combine(root, "proj")),
                Args("--confirm-disposable", "--all", "--library", project),
                Args("--confirm-disposable", "--all", "--pid", "0"),
                Args("--confirm-disposable", "--all", "--disposable-root", Path.Combine(root, "missing")),
            };
            int rejected = 0;
            foreach (var a in rejections) { try { Options.Parse(a); } catch (ArgumentException) { rejected++; } }
            Assert(rejected == rejections.Length, "scope rejections " + rejected + "/" + rejections.Length);
            Program.Emit(new { type = "self-test", success = true, cases = ids.Count, inAll = all.Cases.Length, crashRisk = CrashRisk, scopeRejections = rejected, attached = false, is64BitProcess = Environment.Is64BitProcess });
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
}
