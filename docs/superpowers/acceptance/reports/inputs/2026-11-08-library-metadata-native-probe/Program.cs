using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web.Script.Serialization;
using Siemens.Engineering;
using Siemens.Engineering.Library;

internal static class Program
{
    private const string ApiPath = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    internal static StreamWriter Output;

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var name = new AssemblyName(args.Name);
            if (!name.Name.StartsWith("Siemens.Engineering.", StringComparison.Ordinal)) return null;
            var path = Path.Combine(ApiPath, name.Name + ".dll");
            if (!File.Exists(path)) return null;
            var assembly = Assembly.LoadFrom(path);
            if (assembly.GetName().FullName != name.FullName)
                throw new FileLoadException("Installed Openness assembly identity mismatch.", path);
            return assembly;
        };
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            if (args.Length != 1) throw new ArgumentException("Usage: NativeLibraryMetadataProbe.exe <new-absolute-work-directory> | --self-test");
            var work = ValidateWork(args[0]);
            Directory.CreateDirectory(work);
            using (Output = new StreamWriter(Path.Combine(work, "results.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true })
                return Run(work);
        }
        catch (Exception ex)
        {
            Console.WriteLine(Json.Serialize(new { type = "fatal", error = Error(ex) }));
            return 1;
        }
    }

    internal static string ValidateWork(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || Path.GetPathRoot(path).Length < 3)
            throw new ArgumentException("Work directory must be an absolute Windows path.");
        var work = Path.GetFullPath(path).TrimEnd('\\', '/');
        if (Directory.Exists(work) || File.Exists(work)) throw new ArgumentException("Work directory must not exist; use a new directory for each run.");
        return work;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string work) => new Probe(work).Run();

    internal static void Emit(object value)
    {
        var line = Json.Serialize(value);
        Output.WriteLine(line);
        Console.WriteLine(line);
    }

    internal static object Error(Exception ex) => new
    {
        type = ex.GetType().FullName,
        message = ex.Message,
        inner = ex.InnerException == null ? null : Error(ex.InnerException)
    };

    private static int SelfTest()
    {
        foreach (var path in new[] { "relative", @"\root-relative", @"C:drive-relative", Environment.CurrentDirectory, "" })
        {
            try { ValidateWork(path); }
            catch (ArgumentException) { continue; }
            throw new Exception("Unsafe work path was accepted: " + path);
        }
        ValidateWork(Path.Combine(Path.GetTempPath(), "LibraryMetadataProbe_" + Guid.NewGuid().ToString("N")));
        var author = new string('A', 10000) + "\nline two\r\nline three";
        var roundTrip = Json.Deserialize<Dictionary<string, object>>(Json.Serialize(new { author, unavailable = (string)null }));
        if ((string)roundTrip["author"] != author || roundTrip["unavailable"] != null)
            throw new Exception("JSON must preserve author text and null values.");
        Console.WriteLine("Self-test passed: work-path guards and exact JSON round trip. No Portal started.");
        return 0;
    }
}

internal sealed class Probe
{
    private readonly string work;
    private int failures;
    private Type createType;

    internal Probe(string work) { this.work = work; }

    internal int Run()
    {
        Program.Emit(new { type = "start", probes = new[] { "A1", "A2", "A3", "A4" }, portalMode = "new WithoutUserInterface" });
        using (var portal = Call("setup", "new TiaPortal(WithoutUserInterface)", () => new TiaPortal(TiaPortalMode.WithoutUserInterface)))
        {
            Case("A1", () =>
            {
                GlobalLibrary library;
                try { library = Create(portal, "A1_User", typeof(UserGlobalLibrary), "Metadata probe author"); }
                catch (EngineeringException ex) when (!Fatal(ex))
                {
                    library = Create(portal, "A1_Base", typeof(GlobalLibrary), "Metadata probe author");
                    createType = typeof(GlobalLibrary);
                }
                if (createType == null) createType = typeof(UserGlobalLibrary);
                try { CheckAuthor("A1", library, "Metadata probe author", true); }
                finally { Close("A1", library); }
            });
            Case("A2", () =>
            {
                var library = Call("A2", "Create<UserGlobalLibrary>(directory, name)", () => portal.GlobalLibraries.Create<UserGlobalLibrary>(new DirectoryInfo(work), "A2_Default"));
                try { Call("A2", "Author.get (omitted author)", () => library.Author); }
                finally { Close("A2", library); }
            });
            Case("A3", () =>
            {
                var library = Call("A3", "Create<UserGlobalLibrary>(directory, name)", () => portal.GlobalLibraries.Create<UserGlobalLibrary>(new DirectoryInfo(work), "A3_Metadata"));
                FileInfo path;
                try
                {
                    Metadata("A3_fresh", library);
                    Call<object>("A3", "Save() before read-only reopen", () => { library.Save(); return null; });
                    path = Call("A3", "Path.get", () => library.Path);
                }
                finally { Close("A3", library); }
                library = Call("A3", "Open(path, ReadOnly)", () => portal.GlobalLibraries.Open(path, OpenMode.ReadOnly));
                try { Metadata("A3_readOnly", library); }
                finally { Close("A3", library); }
            });
            Case("A3_system", () =>
            {
                GlobalLibraryInfo system = null;
                Call("A3_system", "GetGlobalLibraryInfos(): first System library", () =>
                {
                    system = portal.GlobalLibraries.GetGlobalLibraryInfos().FirstOrDefault(i => i.LibraryType == GlobalLibraryType.System);
                    return system == null ? null : new { system.Name, system.IsOpen, path = system.Path?.FullName };
                });
                if (system == null) Program.Emit(new { type = "skipped", id = "A3_system", reason = "No installed system library reported by Portal." });
                else Metadata("A3_system", Call("A3_system", "Open(GlobalLibraryInfo)", () => portal.GlobalLibraries.Open(system)));
            });
            foreach (var test in new[] { new { id = "A4_long", author = new string('A', 10000) }, new { id = "A4_lines", author = "First line\nSecond line\r\nThird line" } })
                Case(test.id, () =>
                {
                    if (createType == null) throw new InvalidOperationException("A1 found no usable creation type; A4 cannot be interpreted.");
                    GlobalLibrary library;
                    try { library = Create(portal, test.id, createType, test.author); }
                    catch (EngineeringException ex) when (!Fatal(ex))
                    {
                        Program.Emit(new { type = "nativeAuthorRefusal", id = test.id, error = Program.Error(ex) });
                        return;
                    }
                    try { CheckAuthor(test.id, library, test.author, false); }
                    finally { Close(test.id, library); }
                });
        }
        Program.Emit(new { type = "complete", failures, libraryFiles = Directory.GetFiles(work, "*.al21", SearchOption.AllDirectories).Select(p => p.Substring(work.Length + 1)).ToArray() });
        return failures == 0 ? 0 : 1;
    }

    private GlobalLibrary Create(TiaPortal portal, string id, Type type, string author)
    {
        Program.Emit(new { type = "input", id, createType = type.FullName, author, authorLength = author.Length, commentParameter = "omitted" });
        var result = Call(id, "IEngineeringComposition.Create(" + type.Name + ", Name/TargetDirectory/Author)", () =>
            ((IEngineeringComposition)portal.GlobalLibraries).Create(type, new Dictionary<string, object>
            { ["Name"] = id, ["TargetDirectory"] = new DirectoryInfo(work), ["Author"] = author }));
        return result as GlobalLibrary ?? throw new InvalidOperationException("Create returned no GlobalLibrary; a library may exist on disk. Inspect before retrying.");
    }

    private void CheckAuthor(string id, GlobalLibrary library, string requested, bool required)
    {
        var actual = Call(id, "Author.get", () => library.Author);
        var equal = string.Equals(requested, actual, StringComparison.Ordinal);
        Program.Emit(new { type = "authorComparison", id, expectedLength = requested.Length, actualLength = actual?.Length, equalOrdinal = equal });
        if (required && !equal) throw new InvalidOperationException("Created author did not match the requested author.");
    }

    private void Metadata(string id, GlobalLibrary library)
    {
        Read(id, "Name", () => library.Name);
        Read(id, "Path", () => library.Path?.FullName);
        Read(id, "IsModified", () => library.IsModified);
        Read(id, "IsReadOnly", () => library.IsReadOnly);
        Read(id, "IsWriteProtected", () => library.IsWriteProtected);
        Read(id, "Author", () => library.Author);
        Read(id, "Version", () => library.Version);
        Read(id, "Family", () => library.Family);
        Read(id, "Copyright", () => library.Copyright);
        Read(id, "CreationTime", () => library.CreationTime.ToString("O"));
        Read(id, "LastModified", () => library.LastModified.ToString("O"));
        Read(id, "LastModifiedBy", () => library.LastModifiedBy);
        Read(id, "SizeKilobytes", () => library.Size);
        Read(id, "Comment.Items", () => library.Comment?.Items.Select(i => new { culture = i.Language?.Culture?.Name, text = i.Text }).ToArray());
        Read(id, "LanguageSettings.Languages", () => library.LanguageSettings?.Languages.Select(l => l.Culture?.Name).ToArray());
        Read(id, "LanguageSettings.ActiveLanguages", () => library.LanguageSettings?.ActiveLanguages.Select(l => l.Culture?.Name).ToArray());
        Read(id, "LanguageSettings.EditingLanguage", () => library.LanguageSettings?.EditingLanguage?.Culture?.Name);
        Read(id, "LanguageSettings.ReferenceLanguage", () => library.LanguageSettings?.ReferenceLanguage?.Culture?.Name);
        Read(id, "UsedProducts", () => library.UsedProducts?.Select(p => new { name = p.Name, version = p.Version }).ToArray());
        Read(id, "HistoryEntries", () => library.HistoryEntries?.Select(h => new { dateTime = h.DateTime.ToString("O"), text = h.Text }).ToArray());
    }

    private void Read(string id, string member, Func<object> read)
    {
        try { Call(id, member + ".get", read); }
        catch (Exception ex) when (!Fatal(ex)) { /* Keep the other metadata observations. */ }
    }

    private void Close(string id, GlobalLibrary library)
    {
        if (library is UserGlobalLibrary user)
            Call<object>(id, "Close() (probe-owned library)", () => { user.Close(); return null; });
    }

    private void Case(string id, Action action)
    {
        try { action(); }
        catch (Exception ex) when (!Fatal(ex))
        {
            failures++;
            Program.Emit(new { type = "caseError", id, error = Program.Error(ex) });
        }
    }

    private T Call<T>(string id, string call, Func<T> action)
    {
        Program.Emit(new { type = "call", id, call });
        try
        {
            var value = action();
            object recorded = value;
            if (value is IEngineeringObject || value is TiaPortal) recorded = new { returnedType = value.GetType().FullName };
            if (value is FileInfo file) recorded = file.FullName;
            Program.Emit(new { type = "return", id, call, value = recorded });
            return value;
        }
        catch (Exception ex)
        {
            Program.Emit(new { type = "throw", id, call, error = Program.Error(ex) });
            throw;
        }
    }

    private static bool Fatal(Exception ex) => ex is NonRecoverableException || ex is EngineeringObjectDisposedException || (ex.InnerException != null && Fatal(ex.InnerException));
}
