using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Common;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;

internal static class Program
{
    internal const string ApiPath = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
    internal const string ProbeRoot = @"C:\Users\LCZ\.codex\visualizations\2026\10\29\01a187d0-883d-7d82-b84c-24468920f5cd\native-hmi-probe";
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 20000000, RecursionLimit = 150 };
    private static readonly object OutputGate = new object();
    private static StreamWriter output;
    private static Timer watchdog;
    private static string currentPhase = "initializing";
    internal static string CurrentPhase { get { return currentPhase; } }
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
            if (args.Length == 1 && args[0] == "--list") { Emit(new { cases = Manifest.Ids }); return 0; }
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            var options = Options.Parse(args);
            output = new StreamWriter(options.OutputPath, true, new UTF8Encoding(false)) { AutoFlush = true };
            try
            {
                Emit(new { type = "startup", processId = Process.GetCurrentProcess().Id,
                    is64BitProcess = Environment.Is64BitProcess, apartment = Thread.CurrentThread.GetApartmentState().ToString(),
                    runtime = Environment.Version.ToString(), timeoutSeconds = 900, output = options.OutputPath });
                return Execute(options);
            }
            catch (Exception ex) { Emit(new { type = "fatal", phase = CurrentPhase, error = Error(ex) }); return 1; }
            finally { watchdog?.Dispose(); output.Flush(); output.Dispose(); output = null; }
        }
        catch (Exception ex) { Emit(new { type = "fatal", phase = CurrentPhase, error = Error(ex) }); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Execute(Options options) { return new ProbeRunner(options).Run(); }
    private static int SelfTest()
    {
        if (Manifest.Ids.Distinct(StringComparer.Ordinal).Count() != Manifest.Ids.Length) throw new Exception("Duplicate case.");
        for (int i = 1; i <= 22; i++) if (!Manifest.Ids.Any(id => id == "P" + i || id.StartsWith("P" + i + "-", StringComparison.Ordinal))) throw new Exception("Missing P" + i);
        int rejected = 0;
        foreach (var args in new[] {
            new[] { "--all", "--confirm-disposable", "--pid", "1", "--project", Options.ProjectPath },
            new[] { "--all", "--confirm-disposable", "--pid", "35872", "--project", @"C:\Wrong.ap21" },
            new[] { "--all", "--pid", "35872", "--project", Options.ProjectPath },
            new[] { "--case", "P99", "--confirm-disposable", "--pid", "35872", "--project", Options.ProjectPath }
        }) { try { Options.Parse(args); } catch (ArgumentException) { rejected++; } }
        if (rejected != 4) throw new Exception("Scope rejection self-test failed.");
        int leaveOpenRejected = 0;
        foreach (var selection in new[] { new[] { "--all" }, new[] { "--case", "P12" }, new[] { "--case", "P20" },
            new[] { "--case", "P7" }, new[] { "--case", "P10", "--case", "P13" } })
        {
            var testArgs = selection.Concat(new[] { "--leave-open", "--confirm-disposable", "--pid", "35872", "--project", Options.ProjectPath }).ToArray();
            try { Options.Parse(testArgs); } catch (ArgumentException) { leaveOpenRejected++; }
        }
        if (leaveOpenRejected != 5) throw new Exception("Leave-open selection rejection self-test failed.");
        foreach (var id in Manifest.LeaveOpenIds)
        {
            var option = Options.Parse(new[] { "--case", id, "--leave-open", "--confirm-disposable", "--pid", "35872", "--project", Options.ProjectPath });
            if (!option.LeaveOpen || option.Cases.Length != 1 || option.Cases[0] != id) throw new Exception("Leave-open selection self-test failed: " + id);
        }
        Emit(new { type = "self-test", success = true, cases = Manifest.Ids.Length, numberedCases = 22, scopeRejections = rejected, attached = false,
            leaveOpenSelections = Manifest.LeaveOpenIds.Length, leaveOpenRejections = leaveOpenRejected,
            is64BitProcess = Environment.Is64BitProcess, apartment = Thread.CurrentThread.GetApartmentState().ToString() });
        return 0;
    }
    internal static void Emit(object value)
    {
        lock (OutputGate)
        {
            var line = Json.Serialize(value);
            output?.WriteLine(line);
            output?.Flush();
            Console.WriteLine(line);
            Console.Out.Flush();
        }
    }
    internal static void StartRequest(string phase)
    {
        Progress(phase);
        if (watchdog == null) watchdog = new Timer(_ => {
            Emit(new { type = "timeout", timeoutSeconds = 900, phase = currentPhase, utc = DateTime.UtcNow.ToString("O"),
                message = "Probe request exceeded 15 minutes; exiting only probe process. No dialog was accepted, no retry, Portal not killed." });
            Environment.Exit(124);
        }, null, 900000, Timeout.Infinite);
        else watchdog.Change(900000, Timeout.Infinite);
    }
    internal static void Progress(string phase)
    {
        currentPhase = phase;
        Emit(new { type = "progress", phase, utc = DateTime.UtcNow.ToString("O") });
    }
    internal static object Error(Exception ex)
    {
        var chain = new List<object>();
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            chain.Add(new { type = inner.GetType().FullName, message = inner.Message, stackTrace = inner.StackTrace });
        return new { type = ex.GetType().FullName, message = ex.Message, stackTrace = ex.StackTrace, inner = chain };
    }
}
internal static class Manifest
{
    internal static readonly string[] LeaveOpenIds = { "P10", "P12-discrete", "P12-analog", "P12-table", "P13", "P19", "P20-missing-tag", "P20-missing-member" };
    internal static readonly string[] Ids = {
        "P1", "P2-cross-group", "P2-case", "P3", "P4", "P5-cross-table", "P5-case", "P6", "P7",
        "P8", "P8-case", "P9", "P10", "P11-discrete", "P11-analog", "P12-discrete", "P12-analog", "P12-table",
        "P13", "P14", "P15-update", "P15-create", "P16-create", "P16-update", "P17", "P18", "P19",
        "P20-missing-tag", "P20-missing-member", "P21", "P22"
    };
}
internal sealed class Options
{
    internal const string ProjectPath = @"<disposable-copy>";
    internal const int PortalPid = 35872;
    internal string[] Cases;
    internal string OutputPath;
    internal bool LeaveOpen;
    internal static Options Parse(string[] args)
    {
        bool all = false, confirm = false, leaveOpen = false;
        string selected = null, project = null, output = null;
        int pid = 0, caseSelections = 0;
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--all") all = true;
            else if (arg == "--confirm-disposable") confirm = true;
            else if (arg == "--leave-open") leaveOpen = true;
            else if (arg == "--case" && i + 1 < args.Length) { selected = args[++i]; caseSelections++; }
            else if (arg == "--project" && i + 1 < args.Length) project = args[++i];
            else if (arg == "--output" && i + 1 < args.Length) output = args[++i];
            else if (arg == "--timeout-seconds" && i + 1 < args.Length && args[++i] == "900") { }
            else if (arg == "--pid" && i + 1 < args.Length && int.TryParse(args[++i], out pid)) { }
            else throw new ArgumentException("Unknown/incomplete argument: " + arg);
        }
        if (!confirm || pid != PortalPid || project == null || !string.Equals(Path.GetFullPath(project), ProjectPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Requires --confirm-disposable --pid 35872 --project with exact disposable project path. No attachment was attempted.");
        if (all == (selected != null)) throw new ArgumentException("Specify exactly one of --all or --case CASE.");
        var cases = all ? Manifest.Ids : Manifest.Ids.Where(id => id == selected || id.StartsWith(selected + "-", StringComparison.Ordinal)).ToArray();
        if (cases.Length == 0) throw new ArgumentException("Unknown case: " + selected);
        if (leaveOpen && (all || caseSelections != 1 || cases.Length != 1 || !Manifest.LeaveOpenIds.Contains(selected, StringComparer.Ordinal)))
            throw new ArgumentException("--leave-open requires exactly one accepted variant: " + string.Join(", ", Manifest.LeaveOpenIds) + ". --all and numbered multi-variant selections are refused before attachment.");
        output = output == null ? Path.Combine(Program.ProbeRoot, "results-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl") : Path.GetFullPath(output);
        if (!output.StartsWith(Program.ProbeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must stay under the dedicated probe directory.");
        return new Options { Cases = cases, OutputPath = output, LeaveOpen = leaveOpen };
    }
}
internal sealed class ProbeWorkerRefusalException : Exception
{
    internal ProbeWorkerRefusalException(string message) : base(message) { }
}
internal sealed class ProbeRunner
{
    private readonly Options options;
    private TiaPortal portal;
    private Project project;
    private HmiSoftware hmi;
    private Dictionary<string, object> record;
    private readonly List<string> applied = new List<string>();
    private string stage;
    private string caseId;
    private string newTag;
    private string newAlarm;
    internal ProbeRunner(Options options) { this.options = options; }

    internal int Run()
    {
        Program.StartRequest("portal-processes.before");
        var processes = TiaPortal.GetProcesses().ToList();
        Program.Progress("portal-processes.returned");
        Program.Emit(new { type = "portal-process-inventory", count = processes.Count });
        Program.Progress("portal-process-selection.before");
        var process = processes.SingleOrDefault(p => p.Id == Options.PortalPid);
        Program.Progress("portal-process-selection.returned");
        if (process == null) throw new InvalidOperationException("Exact Portal PID not found; no Portal was started.");
        if (options.LeaveOpen)
        {
            Program.Progress("leave-open.portal-mode.before");
            var mode = process.Mode;
            Program.Progress("leave-open.portal-mode.returned");
            if (mode != TiaPortalMode.WithUserInterface) throw new InvalidOperationException("--leave-open requires the existing GUI Portal so disposing this attachment cannot close a sole-client headless instance.");
            Program.Emit(new { type = "leave-open-mode-verified", mode = mode.ToString() });
        }
        Program.Progress("attach.before");
        using (portal = process.Attach())
        {
            Program.Progress("attach.returned");
            Program.Emit(new { type = "attach-returned", requestedPortalPid = Options.PortalPid });
            Program.Progress("attach.pid-verification.before");
            var attachedPid = portal.GetCurrentProcess().Id;
            Program.Progress("attach.pid-verification.returned");
            if (attachedPid != Options.PortalPid) throw new InvalidOperationException("Attach returned a different Portal PID; no project was selected.");
            Program.Emit(new { type = "attached-pid-verified", portalPid = attachedPid });
            Program.Progress("event-subscribe.confirmation.before");
            portal.Confirmation += OnConfirmation;
            Program.Progress("event-subscribe.confirmation.returned");
            try
            {
                ResolveExistingProject();
                Program.Emit(new { type = "attached", portalPid = Options.PortalPid, project = project.Path.FullName, hmi = hmi.Name,
                    assembly = typeof(HmiSoftware).Assembly.FullName, runtime = Environment.Version.ToString(),
                    manualCompile = "Baseline maintainer reports clean, no errors. Per-case UI compile pending maintainer return.",
                    transactions = "None: mimic current HMI writers and preserve partial mutation/create-cleanup evidence.",
                    exclusiveAccess = "None: exact current HMI worker mutation context; single probe process only.",
                    save = "Never called; exact saved baseline reopened after every variant." });
                foreach (var id in options.Cases)
                {
                    if (!RunCase(id)) return 2;
                }
                if (options.LeaveOpen) Program.Emit(new { type = "complete", cases = 1, project = project.Path.FullName, saved = false, projectLeftOpen = true, restorationSkipped = true });
                else Program.Emit(new { type = "complete", cases = options.Cases.Length, project = project.Path.FullName, saved = false });
                return 0;
            }
            finally
            {
                Program.Progress("event-unsubscribe.confirmation.before");
                portal.Confirmation -= OnConfirmation;
                Program.Progress("event-unsubscribe.confirmation.returned");
            }
        }
    }
    private static void OnConfirmation(object sender, ConfirmationEventArgs e)
    {
        // The only lifecycle action here is closing this explicitly disposable project without saving.
        Program.Emit(new { type = "confirmation-event", phase = Program.CurrentPhase, result = "No", description = e.ToString() });
        e.Result = ConfirmationResult.No;
    }
    private void ResolveExistingProject()
    {
        Program.Progress("project-enumeration.before");
        var all = portal.Projects.ToList();
        Program.Progress("project-enumeration.returned");
        Program.Emit(new { type = "project-inventory", count = all.Count });
        if (all.Count != 1) throw new InvalidOperationException("Portal does not contain exactly the named disposable project; no mutation attempted.");
        Program.Progress("project-path-verification.before");
        var actualPath = Path.GetFullPath(all[0].Path.FullName);
        Program.Progress("project-path-verification.returned");
        if (!string.Equals(actualPath, Options.ProjectPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Portal does not contain exactly the named disposable project; no mutation attempted.");
        project = all[0];
        Program.Emit(new { type = "project-path-verified", project = actualPath });
        var matches = new List<HmiSoftware>();
        Program.Progress("hmi-discovery.before");
        int deviceIndex = 0;
        foreach (var device in AllDevices(project))
        {
            var deviceLabel = "hmi-device-" + deviceIndex++;
            Program.Progress(deviceLabel + ".name.before");
            var deviceName = device.Name;
            Program.Progress(deviceLabel + ".name.returned");
            Program.Emit(new { type = "hmi-discovery-device", index = deviceIndex - 1, name = deviceName });
            int itemIndex = 0;
            foreach (var item in AllItems(device.DeviceItems))
            {
                var itemLabel = deviceLabel + ".item-" + itemIndex++;
                Program.Progress(itemLabel + ".software-container.before");
                var software = item.GetService<SoftwareContainer>()?.Software as HmiSoftware;
                Program.Progress(itemLabel + ".software-container.returned");
                if (software != null)
                {
                    Program.Progress(itemLabel + ".software-name.before");
                    var softwareName = software.Name;
                    Program.Progress(itemLabel + ".software-name.returned");
                    if (softwareName == "HMI_RT_1" && deviceName == "HMI_Wash") matches.Add(software);
                }
            }
        }
        Program.Progress("hmi-discovery.returned");
        if (matches.Count != 1) throw new InvalidOperationException("Exact HMI_RT_1 on HMI_Wash is not unique.");
        hmi = matches[0];
        Program.Emit(new { type = "target-resolved", portalPid = Options.PortalPid, project = actualPath, hmi = "HMI_RT_1", device = "HMI_Wash" });
    }
    private static IEnumerable<Device> AllDevices(Project p)
    {
        Program.Progress("hmi-device-enumeration.root.before");
        foreach (var d in p.Devices) yield return d;
        Program.Progress("hmi-device-enumeration.root.returned");
        Program.Progress("hmi-device-enumeration.ungrouped.before");
        foreach (var d in p.UngroupedDevicesGroup.Devices) yield return d;
        Program.Progress("hmi-device-enumeration.ungrouped.returned");
        Program.Progress("hmi-device-enumeration.groups.before");
        foreach (var d in GroupDevices(p.DeviceGroups)) yield return d;
        Program.Progress("hmi-device-enumeration.groups.returned");
    }
    private static IEnumerable<Device> GroupDevices(IEnumerable<DeviceUserGroup> groups)
    {
        foreach (var group in groups)
        {
            foreach (var device in group.Devices) yield return device;
            foreach (var device in GroupDevices(group.Groups)) yield return device;
        }
    }
    private static IEnumerable<DeviceItem> AllItems(IEnumerable<DeviceItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in AllItems(item.DeviceItems)) yield return child;
        }
    }
    private bool RunCase(string id)
    {
        Program.StartRequest(id + ".case-start-before-snapshot");
        caseId = id;
        newTag = "CP1029_" + id.Replace("-", "_");
        newAlarm = "CP1029_" + id.Replace("-", "_");
        record = new Dictionary<string, object> {
            { "type", "case" }, { "case", id }, { "startedUtc", DateTime.UtcNow.ToString("O") },
            { "nativeMutationReturned", false }, { "exception", null }, { "failureStage", null },
            { "createReturned", false }, { "createReturnMatchesPreexisting", null }, { "cleanup", "not-needed" },
            { "uiCompile", "pending maintainer return" }
        };
        applied.Clear();
        stage = "before";
        object before;
        try { before = Snapshot(); record["before"] = before; }
        catch (Exception ex) { record["beforeError"] = Program.Error(ex); Program.Emit(record); return false; }
        bool fatal = false, harnessError = false;
        try
        {
            ExecuteCase(id);
            record["nativeMutationReturned"] = true;
        }
        catch (Exception ex)
        {
            record["exception"] = Program.Error(ex);
            record["failureStage"] = stage;
            record["exceptionIsSiemensEngineering"] = ex is EngineeringException;
            record["exceptionIsNativeTargetInvocation"] = ex is EngineeringTargetInvocationException;
            record["workerRefusal"] = ex is ProbeWorkerRefusalException;
            fatal = IsFatal(ex);
            harnessError = !fatal && !(ex is EngineeringException) && !(ex is ProbeWorkerRefusalException);
            record["harnessError"] = harnessError;
        }
        record["appliedStages"] = applied.ToArray();
        Program.Progress(id + ".independent-after-snapshot");
        try { record["after"] = Snapshot(); }
        catch (Exception ex) { record["afterError"] = Program.Error(ex); fatal |= IsFatal(ex); }
        if (fatal) { record["stopped"] = "Portal loss; no restore or retry attempted."; Program.Emit(record); return false; }
        if (options.LeaveOpen)
        {
            record["restorationSkipped"] = true;
            record["projectLeftOpen"] = true;
            record["saved"] = false;
            record["endedUtc"] = DateTime.UtcNow.ToString("O");
            if (harnessError) record["stopped"] = "Unexpected harness exception; project left open for inspection.";
            Program.Emit(record);
            return !harnessError;
        }
        try
        {
            stage = "restore-close-without-save";
            Program.Progress(id + "." + stage);
            project.Close();
            stage = "restore-open-baseline";
            Program.Progress(id + "." + stage);
            project = portal.Projects.Open(new FileInfo(Options.ProjectPath));
            ResolveExistingProject();
            var restored = Snapshot();
            record["restored"] = restored;
            var same = Program.Json.Serialize(before) == Program.Json.Serialize(restored);
            record["restoreMatchesBefore"] = same;
            if (!same) { record["stopped"] = "Restored snapshot differs from before-case baseline."; Program.Emit(record); return false; }
        }
        catch (Exception ex)
        {
            record["restoreError"] = Program.Error(ex);
            record["stopped"] = "Restore failed; no later case attempted.";
            Program.Emit(record);
            return false;
        }
        record["endedUtc"] = DateTime.UtcNow.ToString("O");
        if (harnessError) record["stopped"] = "Unexpected harness exception; restored baseline, no later case attempted.";
        Program.Emit(record);
        return !harnessError;
    }
    private static bool IsFatal(Exception ex)
    {
        if (ex is NonRecoverableException) return true;
        return ex.InnerException != null && IsFatal(ex.InnerException);
    }
    private void Set(string name, Action action)
    {
        stage = name;
        Program.Progress(caseId + "." + stage);
        action();
        applied.Add(name);
    }
    private void ExecuteCase(string id)
    {
        switch (id)
        {
            case "P1": Set("TagTables.Create", () => hmi.TagTables.Create("CP1029_TableA")); break;
            case "P2-cross-group": Set("HMI.TagTables.Create", () => Tables("HMI").Create("CP1029_TableA")); break;
            case "P2-case": Set("HMI.TagTables.Create.case", () => Tables("HMI").Create("cp1029_tablea")); break;
            case "P3": Set("Table.Name", () => Table("CP1029_TableB").Name = "CP1029_TableA"); break;
            case "P4": CreateTag("CP1029_TagA", "CP1029_TableA", t => {
                Set("DataType", () => t.DataType = "Bool"); MissingCulture(t.Comment); }); break;
            case "P5-cross-table": CreateTag("CP1029_TagA", "CP1029_TableB", t => Set("DataType", () => t.DataType = "Int")); break;
            case "P5-case": CreateTag("cp1029_taga", "CP1029_TableB", t => Set("DataType", () => t.DataType = "Int")); break;
            case "P6": Set("Tag.Name", () => Tag("CP1029_TagB").Name = "CP1029_TagA"); break;
            case "P7": Set("Tag.Name.case", () => Tag("CP1029_Reuse").Name = "cp1029_reuse"); break;
            case "P8": CreateDiscrete("CP1029_DiscreteA", a => {
                ConfigureDiscrete(a, "CP1029_TagB", 5); MissingCulture(a.EventText); }); break;
            case "P8-case": CreateDiscrete("cp1029_discretea", a => {
                ConfigureDiscrete(a, "CP1029_TagB", 5); MissingCulture(a.EventText); }); break;
            case "P9": CreateAnalog("CP1029_AnalogA", a => {
                ConfigureAnalog(a, "accessLevel", 123); MissingCulture(a.EventText); }); break;
            case "P10": CreateAnalog("CP1029_DiscreteA", a => ConfigureAnalog(a, "AI_CL_M101", 30)); break;
            case "P11-discrete": Set("Discrete.Name", () => Discrete("CP1029_DiscreteB").Name = "CP1029_DiscreteA"); break;
            case "P11-analog": Set("Analog.Name", () => Analog("CP1029_AnalogB").Name = "CP1029_AnalogA"); break;
            case "P12-discrete": Set("Discrete.Name.case", () => Discrete("CP1029_DiscreteB").Name = "cp1029_discreteb"); break;
            case "P12-analog": Set("Analog.Name.case", () => Analog("CP1029_AnalogB").Name = "cp1029_analogb"); break;
            case "P12-table": Set("Table.Name.case", () => Table("CP1029_TableB").Name = "cp1029_tableb"); break;
            case "P13":
                Set("Tag.Delete", () => Tag("CP1029_Reuse").Delete());
                CreateTag("cp1029_reuse", "CP1029_TableA", t => Set("DataType", () => t.DataType = "Int")); break;
            case "P14": Set("DefaultTable.Delete", () => Table("Default tag table").Delete()); break;
            case "P15-update": Set("BoundTag.DataType", () => Tag("CP1029_Bound").DataType = "Bool"); break;
            case "P15-create": CreateTag(newTag, "CP1029_TableA", t => {
                Set("Connection", () => t.Connection = "HMI_Connection_1"); Set("PlcTag", () => t.PlcTag = "AI_CL_M101"); Set("DataType", () => t.DataType = "Bool"); }); break;
            case "P16-create": CreateTag(newTag, "CP1029_TableA", t => Set("PlcTag", () => t.PlcTag = "AI_CL_M101")); break;
            case "P16-update": Set("InternalTag.PlcTag", () => Tag("CP1029_TagA").PlcTag = "AI_CL_M101"); break;
            case "P17": CreateTag(newTag, "CP1029_NoSuchTable", t => Set("DataType", () => t.DataType = "Int")); break;
            case "P18": CreateTag(newTag, "CP1029_TableA", t => {
                Set("Connection", () => t.Connection = "CP1029_NoSuchConnection"); Set("PlcTag", () => t.PlcTag = "AI_CL_M101"); }); break;
            case "P19": CreateDiscrete(newAlarm, a => {
                Set("RaisedStateTag", () => a.RaisedStateTag = "CP1029_TagA");
                Set("RaisedStateTagBitNumber", () => a.RaisedStateTagBitNumber = 0);
                Set("AlarmClass", () => a.AlarmClass = "CP1029_NoSuchClass");
                Text(a.EventText, "<body><p>Checkpoint missing class</p></body>"); }); break;
            case "P20-missing-tag": CreateDiscrete(newAlarm, a => ConfigureDiscrete(a, "CP1029_NoSuchTag", 0)); break;
            case "P20-missing-member": CreateDiscrete(newAlarm, a => ConfigureDiscrete(a, "Communication_DB_MOT01.CP1029_NoSuchMember", 0)); break;
            case "P21":
                stage = "worker-group-resolution";
                var tables = Tables("CP1029_NoSuchGroup");
                Set("TagTables.Create", () => tables.Create("CP1029_P21")); break;
            case "P22": CreateTag(newTag, "CP1029_TableA", t => {
                Set("DataType", () => t.DataType = "Int"); MissingCulture(t.Comment); }); break;
            default: throw new InvalidOperationException("Unknown manifest case: " + id);
        }
    }
    private void ConfigureDiscrete(HmiDiscreteAlarm alarm, string trigger, uint bit)
    {
        Set("RaisedStateTag", () => alarm.RaisedStateTag = trigger);
        Set("RaisedStateTagBitNumber", () => alarm.RaisedStateTagBitNumber = bit);
        Set("AlarmClass", () => alarm.AlarmClass = "Alarm");
        Text(alarm.EventText, "<body><p>Checkpoint discrete</p></body>");
    }
    private void ConfigureAnalog(HmiAnalogAlarm alarm, string trigger, int value)
    {
        Set("RaisedStateTag", () => alarm.RaisedStateTag = trigger);
        Set("Condition", () => alarm.Condition = HmiAlarmCondition.UpperLimit);
        Set("ConditionValue.Int32", () => alarm.ConditionValue = value);
        Set("AlarmClass", () => alarm.AlarmClass = "Warning");
        Text(alarm.EventText, "<body><p>Checkpoint analog</p></body>");
    }
    private void Text(MultilingualText text, string value)
    {
        Set("EventText.en-GB", () => {
            var item = text.Items.FirstOrDefault(i => i.Language.Culture.Name == "en-GB");
            if (item == null) throw new ProbeWorkerRefusalException("Text culture 'en-GB' was not found.");
            item.Text = value;
        });
    }
    private void MissingCulture(MultilingualText text)
    {
        Set("worker-text-culture-lookup.zu-ZA", () => {
            var item = text.Items.FirstOrDefault(i => string.Equals(i.Language.Culture.Name, "zu-ZA", StringComparison.OrdinalIgnoreCase));
            if (item == null) throw new ProbeWorkerRefusalException("Text culture 'zu-ZA' was not found.");
            item.Text = "Checkpoint missing culture";
        });
    }
    private void CreateTag(string name, string table, Action<HmiTag> apply)
    {
        Create(name, () => hmi.Tags.Create(name, table), () => hmi.Tags.Find(name), t => t.Delete(), apply,
            hmi.Tags.Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToList());
    }
    private void CreateDiscrete(string name, Action<HmiDiscreteAlarm> apply)
    {
        Create(name, () => hmi.DiscreteAlarms.Create(name), () => hmi.DiscreteAlarms.Find(name), a => a.Delete(), apply,
            hmi.DiscreteAlarms.Where(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToList());
    }
    private void CreateAnalog(string name, Action<HmiAnalogAlarm> apply)
    {
        Create(name, () => hmi.AnalogAlarms.Create(name), () => hmi.AnalogAlarms.Find(name), a => a.Delete(), apply,
            hmi.AnalogAlarms.Where(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToList());
    }
    private void Create<T>(string name, Func<T> create, Func<T> find, Action<T> delete, Action<T> apply, List<object> preexisting) where T : class
    {
        record["preexistingNameMatches"] = preexisting.Count;
        T created = null;
        try
        {
            Set("Create", () => created = create());
            record["createReturned"] = true;
            record["createReturnMatchesPreexisting"] = preexisting.Any(old => object.Equals(old, created));
            Program.Progress(caseId + ".after-create-observation");
            try { record["afterCreate"] = Snapshot(); }
            catch (Exception snapshotError) { if (IsFatal(snapshotError)) throw; record["afterCreateError"] = Program.Error(snapshotError); }
            apply(created);
        }
        catch (Exception original) when (original is EngineeringException || original is ProbeWorkerRefusalException || IsFatal(original))
        {
            if (IsFatal(original)) throw;
            var failureStage = stage;
            record["mutationExceptionBeforeCleanup"] = Program.Error(original);
            record["failureStageBeforeCleanup"] = failureStage;
            Program.Progress(caseId + ".before-cleanup-observation");
            try { record["beforeCleanup"] = Snapshot(); }
            catch (Exception snapshotError) { if (IsFatal(snapshotError)) throw; record["beforeCleanupError"] = Program.Error(snapshotError); }
            if (created != null)
            {
                try
                {
                    stage = "cleanup.Find";
                    Program.Progress(caseId + "." + stage);
                    var found = find();
                    if (found != null)
                    {
                        var same = object.Equals(found, created);
                        record["cleanupFindMatchesCreated"] = same;
                        if (!same) throw new InvalidOperationException("Created object identity changed; cleanup refused.");
                        stage = "cleanup.Delete";
                        Program.Progress(caseId + "." + stage);
                        delete(found);
                        record["cleanup"] = "deleted";
                    }
                }
                catch (Exception cleanupError)
                {
                    if (IsFatal(cleanupError)) throw;
                    record["cleanup"] = "failed";
                    record["cleanupError"] = Program.Error(cleanupError);
                }
            }
            stage = failureStage;
            throw;
        }
    }
    private HmiTag Tag(string name) { return hmi.Tags.Single(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)); }
    private HmiDiscreteAlarm Discrete(string name) { return hmi.DiscreteAlarms.Single(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)); }
    private HmiAnalogAlarm Analog(string name) { return hmi.AnalogAlarms.Single(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)); }
    private HmiTagTable Table(string name) { return AllTables(hmi.TagTables, hmi.TagTableGroups).Single(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)); }
    private HmiTagTableComposition Tables(string path)
    {
        var tables = hmi.TagTables;
        var groups = hmi.TagTableGroups;
        foreach (var segment in path.Split('/'))
        {
            var group = groups.Find(segment);
            if (group == null) throw new ProbeWorkerRefusalException("Tag table group '" + path + "' was not found.");
            tables = group.TagTables;
            groups = group.Groups;
        }
        return tables;
    }
    private static IEnumerable<HmiTagTable> AllTables(IEnumerable<HmiTagTable> tables, IEnumerable<HmiTagTableGroup> groups)
    {
        foreach (var table in tables) yield return table;
        foreach (var group in groups) foreach (var table in AllTables(group.TagTables, group.Groups)) yield return table;
    }
    private object Snapshot()
    {
        var names = new HashSet<string>(new[] { "CP1029_TagA", "CP1029_TagB", "CP1029_Bound", "CP1029_Reuse", newTag,
            "AI_CL_M101", "accessLevel", "Communication_DB_MOT01" }, StringComparer.OrdinalIgnoreCase);
        var allTags = hmi.Tags.ToList();
        var tags = allTags.Where(t => names.Contains(t.Name) || (caseId == "P14" && t.TagTableName == "Default tag table"))
            .OrderBy(t => t.Name, StringComparer.Ordinal).Select(TagState).ToList();
        var tables = new List<object>();
        TableStates(hmi.TagTables, hmi.TagTableGroups, "", tables);
        return new {
            projectPath = project.Path.FullName, hmi = hmi.Name, tagCount = allTags.Count,
            allTagNames = allTags.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            tags, tables,
            discrete = hmi.DiscreteAlarms.OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => AlarmState(a, "discrete")).ToList(),
            analog = hmi.AnalogAlarms.OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => AlarmState(a, "analog")).ToList()
        };
    }
    private static void TableStates(IEnumerable<HmiTagTable> tables, IEnumerable<HmiTagTableGroup> groups, string path, List<object> output)
    {
        foreach (var table in tables.OrderBy(t => t.Name, StringComparer.Ordinal))
            output.Add(new { group = path, name = table.Name, tags = table.Tags.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(), validate = "not supported by native HmiTagTable" });
        foreach (var group in groups.OrderBy(g => g.Name, StringComparer.Ordinal))
            TableStates(group.TagTables, group.Groups, path.Length == 0 ? group.Name : path + "/" + group.Name, output);
    }
    private static object TagState(HmiTag tag)
    {
        return new {
            name = Read(() => tag.Name), table = Read(() => tag.TagTableName),
            dataType = Read(() => tag.DataType), hmiDataType = Read(() => tag.HmiDataType),
            connection = Read(() => tag.Connection), plcTag = Read(() => tag.PlcTag), plcName = Read(() => tag.PlcName),
            acquisitionCycle = Read(() => tag.AcquisitionCycle), comment = Read(() => TextState(tag.Comment)), validate = Validate(tag)
        };
    }
    private static object AlarmState(AlarmBase alarm, string kind)
    {
        var discrete = alarm as HmiDiscreteAlarm;
        var analog = alarm as HmiAnalogAlarm;
        return new {
            kind, name = Read(() => discrete != null ? discrete.Name : analog.Name),
            triggerTag = Read(() => alarm.RaisedStateTag), alarmClass = Read(() => alarm.AlarmClass), priority = Read(() => alarm.Priority),
            triggerBit = discrete == null ? null : Read(() => discrete.RaisedStateTagBitNumber),
            condition = analog == null ? null : Read(() => analog.Condition.ToString()),
            conditionValue = analog == null ? null : Read(() => new { type = analog.ConditionValue?.GetType().FullName, value = analog.ConditionValue }),
            eventText = Read(() => TextState(alarm.EventText)), infoText = Read(() => TextState(alarm.InfoText)), validate = Validate(alarm)
        };
    }
    private static object TextState(MultilingualText text)
    {
        return text.Items.Select(i => new { culture = i.Language.Culture.Name, text = i.Text }).OrderBy(i => i.culture, StringComparer.Ordinal).ToArray();
    }
    private static object Read(Func<object> read)
    {
        try { return read(); }
        catch (Exception ex) { if (IsFatal(ex)) throw; return new { readError = Program.Error(ex) }; }
    }
    private static object Validate(object item)
    {
        var validator = item as IValidator;
        if (validator == null) return new { supported = false };
        try
        {
            var raw = validator.Validate();
            if (raw == null) return new { supported = true, complete = false, reason = "null result" };
            var results = raw.Select(r => new { property = r.PropertyName, errors = r.Errors?.ToArray(), warnings = r.Warnings?.ToArray() }).ToArray();
            bool complete = results.All(r => r.errors != null && r.warnings != null);
            return new { supported = true, complete, clean = complete && results.All(r => r.errors.Length == 0 && r.warnings.Length == 0), results };
        }
        catch (Exception ex) { if (IsFatal(ex)) throw; return new { supported = true, complete = false, error = Program.Error(ex) }; }
    }
}
