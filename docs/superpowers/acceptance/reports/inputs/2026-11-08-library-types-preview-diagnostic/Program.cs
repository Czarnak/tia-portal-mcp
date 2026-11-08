using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

internal static class Program
{
    private const string ApiPath = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var name = new AssemblyName(args.Name);
            if (!name.Name.StartsWith("Siemens.Engineering.", StringComparison.Ordinal)) return null;
            var path = Path.Combine(ApiPath, name.Name + ".dll");
            if (!File.Exists(path)) return null;
            var loaded = Assembly.LoadFrom(path);
            if (name.FullName != loaded.GetName().FullName) throw new FileLoadException("Installed Openness identity mismatch.");
            return loaded;
        };
    }

    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine(Json.Serialize(new { success = false, errorType = ex.GetType().FullName, message = ex.Message }));
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        if (args.Length == 1 && args[0] == "metadata")
        {
            Console.WriteLine(Json.Serialize(new { typeNameCanWrite = typeof(LibraryType).GetProperty("Name").CanWrite,
                blockNameCanWrite = typeof(PlcBlock).GetProperty("Name").CanWrite }));
            return 0;
        }
        var versionAction = args.Length == 3 && (args[0] == "inspect-global-version" || args[0] == "inspect-project-version");
        if (args.Length != 2 && args.Length != 4 && !versionAction)
            throw new ArgumentException("Usage: metadata | inspect[-block] <selector> | rename[-block] <selector> <expectedOldName> <newName> | inspect-{global|project}-version <typeGuid> <versionNumber>");
        var blockAction = args[0].EndsWith("-block", StringComparison.Ordinal);
        var action = blockAction ? args[0].Substring(0, args[0].Length - 6) : args[0];
        if (action != "inspect" && action != "rename" && !versionAction) throw new ArgumentException("Unknown action.");
        if (action == "rename" && (args.Length != 4 || !args[2].StartsWith("LT_", StringComparison.Ordinal) || !args[3].StartsWith("LT_", StringComparison.Ordinal)))
            throw new ArgumentException("Rename requires exact old/new LT_ fixture names.");
        var portalId = int.Parse(Environment.GetEnvironmentVariable("LT_PORTAL_PID") ?? throw new ArgumentException("LT_PORTAL_PID required."));
        var expectedPath = Path.GetFullPath(Environment.GetEnvironmentVariable("LT_PROJECT") ?? throw new ArgumentException("LT_PROJECT required."));
        var process = TiaPortal.GetProcesses().Single(p => p.Id == portalId);
        using (var portal = process.Attach())
        {
            var project = portal.Projects.Single(p => string.Equals(p.Path.FullName, expectedPath, StringComparison.OrdinalIgnoreCase));
            if (versionAction)
            {
                ILibrary library = project.ProjectLibrary;
                if (args[0] == "inspect-global-version")
                {
                    var libraryPath = Environment.GetEnvironmentVariable("LT_LIBRARY") ?? throw new ArgumentException("LT_LIBRARY required.");
                    if (!Path.IsPathRooted(libraryPath)) throw new ArgumentException("LT_LIBRARY must be absolute.");
                    libraryPath = Path.GetFullPath(libraryPath);
                    library = portal.GlobalLibraries.Single(l => string.Equals(l.Path.FullName, libraryPath, StringComparison.OrdinalIgnoreCase));
                }
                var selectedType = Types(library.TypeFolder).Single(t => t.Guid == Guid.Parse(args[1]));
                var selectedVersion = selectedType.Versions.Single(v => v.VersionNumber == Version.Parse(args[2]));
                Console.WriteLine(Json.Serialize(new { action, typeGuid = selectedType.Guid, typeName = selectedType.Name,
                    versionGuid = selectedVersion.Guid, versionNumber = selectedVersion.VersionNumber.ToString() }));
                foreach (var plc in project.Devices.SelectMany(d => Plcs(d.DeviceItems)))
                {
                    var plcName = plc.Name;
                    Probe("LibraryTypeVersion.FindInstances(" + plcName + ")", () => selectedVersion.FindInstances(plc)
                        .Select(i => new { instance = ReadField(() => i.LibraryTypeInstance.GetAttribute("Name")),
                            nativeType = ReadField(() => i.LibraryTypeInstance.GetType().FullName) }).ToArray());
                }
                MasterCopyAssociation copies = null;
                Probe("LibraryTypeVersion.MasterCopiesContainingInstances.get", () =>
                {
                    copies = selectedVersion.MasterCopiesContainingInstances;
                    return new { nativeType = copies.GetType().FullName };
                });
                if (copies != null)
                    Probe("MasterCopyAssociation.GetEnumerator/MoveNext", () => copies.Select(m => new
                    { name = ReadField(() => m.Name), nativeType = m.GetType().FullName }).ToArray());
                return 0;
            }
            IEngineeringObject target = blockAction ? Block(project, args[1])
                : (IEngineeringObject)Types(project.ProjectLibrary.TypeFolder).Single(t => t.Guid == Guid.Parse(args[1]));
            var attributes = target.GetAttributeInfos();
            var before = (string)target.GetAttribute("Name");
            Console.WriteLine(Json.Serialize(new { selector = args[1], name = before,
                attributes = attributes.Select(a => new { a.Name, access = a.AccessMode.ToString() }).ToArray() }));
            if (action == "inspect") return 0;
            if (before != args[2]) throw new InvalidOperationException("Type name changed; no rename attempted.");
            var nameInfo = attributes.SingleOrDefault(a => a.Name == "Name");
            if (nameInfo == null || (nameInfo.AccessMode & EngineeringAttributeAccessMode.Write) == 0)
                throw new InvalidOperationException("Native metadata does not advertise writable Name; no rename attempted.");
            target.SetAttribute("Name", args[3]);
            var after = (string)target.GetAttribute("Name");
            if (after != args[3]) throw new InvalidOperationException("Native rename did not produce the requested name.");
            Console.WriteLine(Json.Serialize(new { success = true, selector = args[1], before, after }));
            return 0;
        }
    }

    private static IEnumerable<LibraryType> Types(LibraryTypeFolder folder)
    {
        foreach (var type in folder.Types) yield return type;
        foreach (var child in folder.Folders)
            foreach (var type in Types(child)) yield return type;
    }

    private static IEngineeringObject Block(Project project, string path)
    {
        var parts = path.Split('/');
        if (parts.Length < 2) throw new ArgumentException("Block selector requires PLC/name.");
        var plc = project.Devices.SelectMany(d => Plcs(d.DeviceItems)).Single(p => p.Name == parts[0]);
        PlcBlockGroup group = plc.BlockGroup;
        for (var i = 1; i < parts.Length - 1; i++) group = group.Groups.Single(g => g.Name == parts[i]);
        return group.Blocks.Single(b => b.Name == parts[parts.Length - 1]);
    }

    private static IEnumerable<PlcSoftware> Plcs(DeviceItemComposition items)
    {
        foreach (var item in items)
        {
            if (item.GetService<SoftwareContainer>()?.Software is PlcSoftware plc) yield return plc;
            foreach (var child in Plcs(item.DeviceItems)) yield return child;
        }
    }

    private static void Probe(string method, Func<object> read)
    {
        try { Console.WriteLine(Json.Serialize(new { method, success = true, rows = read() })); }
        catch (Exception ex) { Console.WriteLine(Json.Serialize(new { method, success = false, error = Error(ex) })); }
    }

    private static object ReadField(Func<object> read)
    {
        try { return new { success = true, value = read() }; }
        catch (Exception ex) { return new { success = false, error = Error(ex) }; }
    }

    private static object Error(Exception ex) => new { type = ex.GetType().FullName, message = ex.Message,
        stackTrace = ex.StackTrace, inner = ex.InnerException == null ? null : Error(ex.InnerException) };
}
