using System;
using System.Collections.Generic;
using System.Globalization;
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
    internal const string RoundTripText = "  First <line> & text\nZażółć 日本語\r\nLast line  ";
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static StreamWriter output;

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
            var longComment = args.Length == 2 && args[0] == "--long-comment";
            if (args.Length != 1 && !longComment)
                throw new ArgumentException("Usage: NativeLibraryMetadataPrBProbe.exe <new-absolute-work-directory> | --long-comment <new-absolute-work-directory> | --self-test");
            var work = ValidateWork(args[longComment ? 1 : 0]);
            Directory.CreateDirectory(work);
            using (output = new StreamWriter(Path.Combine(work, "results.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true })
            {
                try { return Run(work, longComment); }
                catch (Exception ex) { Emit(new { type = "fatal", error = Error(ex) }); return 1; }
            }
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
        if (Directory.Exists(work) || File.Exists(work))
            throw new ArgumentException("Work directory must not exist; use a new directory for each run.");
        return work;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string work, bool longComment) => new Probe(work).Run(longComment);

    internal static void Emit(object value)
    {
        var line = Json.Serialize(value);
        output.WriteLine(line);
        Console.WriteLine(line);
    }

    internal static object Error(Exception ex) => new
    {
        type = ex.GetType().FullName,
        message = ex.Message,
        inner = ex.InnerException == null ? null : Error(ex.InnerException)
    };

    internal static bool Same(object before, object after) => Json.Serialize(before) == Json.Serialize(after);

    private static int SelfTest()
    {
        foreach (var path in new[] { "relative", @"\root-relative", @"C:drive-relative", Environment.CurrentDirectory, "" })
        {
            try { ValidateWork(path); }
            catch (ArgumentException) { continue; }
            throw new Exception("Unsafe work path was accepted: " + path);
        }
        ValidateWork(Path.Combine(Path.GetTempPath(), "LibraryMetadataPrB_" + Guid.NewGuid().ToString("N")));
        foreach (var text in new[] { "", RoundTripText, new string('C', 100000) })
        {
            var decoded = Json.Deserialize<Dictionary<string, object>>(Json.Serialize(new { text, missing = (string)null }));
            if (!string.Equals(text, (string)decoded["text"], StringComparison.Ordinal) || decoded["missing"] != null)
                throw new Exception("JSON changed comment text or null.");
        }
        if (!Same(new { text = RoundTripText }, new { text = RoundTripText }) || Same(new { text = "" }, new { text = " " }))
            throw new Exception("Snapshot comparison lost an exact text change.");
        Console.WriteLine("Self-test passed: work-path guards, exact JSON text/null round trips and snapshot comparison. No Portal started.");
        return 0;
    }
}

internal sealed class Probe
{
    private readonly string work;
    private int failures;
    private TiaPortal portal;
    private bool portalLost;
    internal Probe(string work) { this.work = work; }

    internal int Run(bool longComment)
    {
        Program.Emit(new { type = "start", mode = longComment ? "B8 only" : "B1-B7, B9-B10", portalMode = "new WithoutUserInterface" });
        using (portal = Call("setup", "new TiaPortal(WithoutUserInterface)", () => new TiaPortal(TiaPortalMode.WithoutUserInterface)))
        {
            if (longComment) Fixture("B8", library => CommentRoundTrip("B8", library, new string('C', 100000)), nativeRefusal: true);
            else
            {
                Fixture("B1", library =>
                {
                    Check(!Call("B1", "IsModified before", () => library.IsModified), "B1 fixture must start clean.");
                    CommentRoundTrip("B1", library, "PR B active-language comment");
                    Check(Call("B1", "IsModified after commit", () => library.IsModified), "B1 commit did not mark the library modified.");
                });
                Fixture("B2", library =>
                {
                    var language = Inactive("B2", library);
                    Edit("B2", library, transaction =>
                    {
                        var item = Call("B2", "Comment.Items.Find(inactive " + language.Culture.Name + ")", () => library.Comment.Items.Find(language));
                        if (item == null) return;
                        Call("B2", "inactive comment Text.set", () => item.Text = "PR B inactive-language comment");
                        Call("B2", "inactive comment Text.get before commit", () => item.Text);
                    });
                }, nativeRefusal: true);
                Fixture("B3", library =>
                {
                    CommentRoundTrip("B3_seed", library, "Clear this comment");
                    var language = Active("B3", library);
                    Edit("B3", library, transaction =>
                    {
                        Call("B3", "comment Text.set empty", () => library.Comment.Items.Find(language).Text = "");
                        Comment("B3", library, language, "before commit");
                    });
                    Comment("B3", library, language, "after commit");
                });
                Fixture("B4", library =>
                {
                    var language = Inactive("B4", library);
                    Comment("B4", library, language, "before activation");
                    Edit("B4", library, transaction =>
                    {
                        Call("B4", "ActiveLanguages.Add(" + language.Culture.Name + ")", () => library.LanguageSettings.ActiveLanguages.Add(language));
                        Comment("B4", library, language, "after activation, before commit");
                    });
                    Comment("B4", library, language, "after commit");
                });
                foreach (var editing in new[] { true, false })
                {
                    var id = editing ? "B5_editing" : "B5_reference";
                    Fixture(id, library =>
                    {
                        var language = Inactive(id, library);
                        Edit(id, library, transaction =>
                        {
                            Call(id, (editing ? "EditingLanguage" : "ReferenceLanguage") + ".set " + language.Culture.Name, () =>
                            {
                                if (editing) library.LanguageSettings.EditingLanguage = language;
                                else library.LanguageSettings.ReferenceLanguage = language;
                            });
                            State(id, library, "before commit");
                        });
                    }, nativeRefusal: true);
                }
                Fixture("B6", library => ReadOnly(library), readOnly: true);
                Fixture("B7", library =>
                {
                    var before = State("B7", library, "before transaction");
                    var active = Active("B7", library);
                    var inactive = Inactive("B7", library);
                    try
                    {
                        Edit("B7", library, transaction =>
                        {
                            Call("B7", "comment Text.set", () => library.Comment.Items.Find(active).Text = "Must roll back");
                            Call("B7", "ActiveLanguages.Add", () => library.LanguageSettings.ActiveLanguages.Add(inactive));
                            Call("B7", "EditingLanguage.set", () => library.LanguageSettings.EditingLanguage = inactive);
                            Call("B7", "ReferenceLanguage.set", () => library.LanguageSettings.ReferenceLanguage = inactive);
                            State("B7", library, "after setters, before forced throw");
                            Call("B7", "CanCommit after setters", () => transaction.CanCommit);
                            Call("B7", "throw before commit", () => { throw new ProbeAbort(); });
                        });
                    }
                    catch (ProbeAbort) { Program.Emit(new { type = "forcedRollback", id = "B7" }); }
                    var after = State("B7", library, "after rollback disposal");
                    Check(Program.Same(before, after), "B7 rollback changed comments or language settings.");
                });
                Fixture("B9", library =>
                {
                    var supported = library.LanguageSettings.Languages.Select(l => l.Culture.Name).ToArray();
                    var unsupported = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                        .OrderBy(c => c.Name, StringComparer.Ordinal).First(c => !supported.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
                    foreach (var culture in new[] { unsupported, CultureInfo.GetCultureInfo("en") })
                    {
                        try { Call("B9", "Languages.Find(" + culture.Name + ")", () => library.LanguageSettings.Languages.Find(culture)?.Culture?.Name); }
                        catch (Exception ex) when (!portalLost && !Fatal(ex)) { Program.Emit(new { type = "nativeRefusal", id = "B9", culture = culture.Name, error = Program.Error(ex) }); }
                    }
                });
                Fixture("B10", library => CommentRoundTrip("B10", library, Program.RoundTripText));
            }
        }
        Program.Emit(new { type = "complete", failures });
        return failures == 0 ? 0 : 1;
    }

    private void Fixture(string id, Action<UserGlobalLibrary> action, bool nativeRefusal = false, bool readOnly = false)
    {
        try
        {
            var library = Call(id, "Create<UserGlobalLibrary>(probe directory)", () => portal.GlobalLibraries.Create<UserGlobalLibrary>(new DirectoryInfo(work), id));
            try
            {
                Call(id, "Save fresh fixture", () => library.Save());
                if (readOnly)
                {
                    var path = Call(id, "Path.get", () => library.Path);
                    Call(id, "Close fresh fixture before reopen", () => library.Close());
                    library = null;
                    library = (UserGlobalLibrary)Call(id, "Open(path, ReadOnly)", () => portal.GlobalLibraries.Open(path, OpenMode.ReadOnly));
                }
                State(id, library, "before");
                try { action(library); }
                catch (EngineeringException ex) when (nativeRefusal && !portalLost && !Fatal(ex))
                { Program.Emit(new { type = "nativeRefusal", id, error = Program.Error(ex) }); }
                State(id, library, "after");
            }
            finally { if (library != null && !portalLost) Call(id, "Close probe-owned library", () => library.Close()); }
        }
        catch (Exception ex) when (!portalLost && !Fatal(ex))
        {
            failures++;
            Program.Emit(new { type = "caseError", id, error = Program.Error(ex) });
        }
    }

    private void ReadOnly(UserGlobalLibrary readOnly)
    {
        Check(Call("B6", "IsReadOnly.get", () => readOnly.IsReadOnly), "B6 library was not opened read-only.");
        foreach (var comments in new[] { true, false })
        {
            var id = comments ? "B6_comments" : "B6_languages";
            var before = State(id, readOnly, "before attempt");
            try
            {
                Edit(id, readOnly, transaction =>
                {
                    if (comments) Call(id, "comment Text.set", () => readOnly.Comment.Items.Find(Active(id, readOnly)).Text = "Must be refused");
                    else Call(id, "ActiveLanguages.Add", () => readOnly.LanguageSettings.ActiveLanguages.Add(Inactive(id, readOnly)));
                });
                throw new InvalidOperationException("B6 read-only edit unexpectedly completed.");
            }
            catch (EngineeringException ex) when (!portalLost && !Fatal(ex))
            { Program.Emit(new { type = "nativeRefusal", id, error = Program.Error(ex) }); }
            Check(Program.Same(before, State(id, readOnly, "after refused attempt")), "B6 read-only attempt changed metadata.");
        }
    }

    private void CommentRoundTrip(string id, GlobalLibrary library, string text)
    {
        var language = Active(id, library);
        Program.Emit(new { type = "input", id, culture = language.Culture.Name, text, length = text.Length });
        Edit(id, library, transaction =>
        {
            Call(id, "comment Text.set", () => library.Comment.Items.Find(language).Text = text);
            CheckText(id, library, language, text, "before commit");
        });
        CheckText(id, library, language, text, "after commit disposal");
    }

    private void CheckText(string id, GlobalLibrary library, Language language, string text, string stage)
    {
        var actual = Call(id, "comment Text.get " + stage, () => library.Comment.Items.Find(language)?.Text);
        var equal = string.Equals(text, actual, StringComparison.Ordinal);
        Program.Emit(new { type = "textComparison", id, stage, equalOrdinal = equal, expectedLength = text.Length, actualLength = actual?.Length });
        Check(equal, id + " comment did not round-trip " + stage + ".");
    }

    private void Comment(string id, GlobalLibrary library, Language language, string stage) => Call(id, "comment item " + stage, () =>
    {
        var item = library.Comment.Items.Find(language);
        return new { culture = language.Culture.Name, exists = item != null, text = item?.Text };
    });

    private Language Active(string id, GlobalLibrary library) => Call(id, "select active language", () =>
        library.LanguageSettings.ActiveLanguages.FirstOrDefault() ?? throw new InvalidOperationException("No active language in fixture."));

    private Language Inactive(string id, GlobalLibrary library) => Call(id, "select supported inactive language", () =>
    {
        var settings = library.LanguageSettings;
        var active = settings.ActiveLanguages.Select(l => l.Culture.Name).ToArray();
        return settings.Languages.FirstOrDefault(l => !active.Contains(l.Culture.Name, StringComparer.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No supported inactive language in fixture.");
    });

    private object State(string id, GlobalLibrary library, string stage)
    {
        Call(id, "IsModified " + stage, () => library.IsModified);
        return Call(id, "metadata snapshot " + stage, () => new
        {
            comments = library.Comment.Items.Select(i => new { culture = i.Language?.Culture?.Name, text = i.Text }).OrderBy(i => i.culture, StringComparer.Ordinal).ToArray(),
            supported = library.LanguageSettings.Languages.Select(l => l.Culture.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            active = library.LanguageSettings.ActiveLanguages.Select(l => l.Culture.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            editing = library.LanguageSettings.EditingLanguage?.Culture?.Name,
            reference = library.LanguageSettings.ReferenceLanguage?.Culture?.Name
        });
    }

    private void Edit(string id, GlobalLibrary library, Action<Transaction> setters)
    {
        try
        {
            using (var access = Call(id, "ExclusiveAccess", () => portal.ExclusiveAccess("Metadata probe " + id)))
            using (var transaction = Call(id, "Transaction(library)", () => access.Transaction(library, "Metadata probe " + id)))
            {
                Call(id, "CanCommit before setters", () => transaction.CanCommit);
                setters(transaction);
                Call(id, "CanCommit after setters", () => transaction.CanCommit);
                Call(id, "CommitOnDispose", () => transaction.CommitOnDispose());
            }
        }
        catch (Exception ex)
        {
            // Disposal can fail outside Call; stop before fixture cleanup on fatal loss.
            portalLost |= Fatal(ex);
            Program.Emit(new { type = "transactionScopeError", id, error = Program.Error(ex) });
            throw;
        }
        Program.Emit(new { type = "transactionDisposed", id });
    }

    private T Call<T>(string id, string call, Func<T> action)
    {
        Program.Emit(new { type = "call", id, call });
        try
        {
            var value = action();
            object recorded = value;
            if (value is IEngineeringObject || value is TiaPortal || value is IDisposable)
                recorded = new { returnedType = value.GetType().FullName };
            if (value is FileInfo file) recorded = file.FullName;
            Program.Emit(new { type = "return", id, call, value = recorded });
            return value;
        }
        catch (Exception ex)
        {
            portalLost |= Fatal(ex);
            Program.Emit(new { type = "throw", id, call, error = Program.Error(ex) });
            throw;
        }
    }

    private void Call(string id, string call, Action action) => Call<object>(id, call, () => { action(); return null; });
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static bool Fatal(Exception ex) => ex is NonRecoverableException || ex is EngineeringObjectDisposedException || (ex.InnerException != null && Fatal(ex.InnerException));
    private sealed class ProbeAbort : Exception { }
}
