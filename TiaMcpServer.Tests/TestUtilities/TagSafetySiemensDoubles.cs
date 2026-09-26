// Offline boundary doubles for source-linked readers and the project snapshot walker.
// These model object access only; no Openness assemblies or processes are loaded.
using System.Collections;

namespace Siemens.Engineering
{
    public abstract class NamedObject
    {
        public string Name { get; set; } = string.Empty;
    }

    public class Composition<T> : IEnumerable<T> where T : NamedObject
    {
        public List<T> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public T? Find(string name) => this.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in Items)
                yield return item;
            if (EnumerationFailure is not null)
                throw EnumerationFailure;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class Project
    {
        public Composition<HW.Device> Devices { get; } = new();
        public Composition<HW.DeviceUserGroup> DeviceGroups { get; } = new();
    }

    public enum ExportOptions { None }
    public enum DocumentInfoOptions { None }
    public class EngineeringException : Exception
    {
        public EngineeringException() { }
        public EngineeringException(string message) : base(message) { }
    }

    public interface IEngineeringServiceProvider
    {
        T? GetService<T>() where T : class;
    }
}

namespace Siemens.Engineering.Compiler
{
    public interface ICompilable
    {
        CompilerResult Compile();
    }

    public enum CompilerResultState { Success, Warning, Error }

    public sealed class CompilerResult
    {
        public CompilerResultState State { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public List<CompilerResultMessage> Messages { get; } = new();
    }

    public class CompilerResultMessage
    {
        public string Description { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public CompilerResultState State { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public List<CompilerResultMessage> Messages { get; } = new();
    }
}

namespace Siemens.Engineering.HW
{
    public sealed class Device : NamedObject
    {
        public DeviceItemComposition DeviceItems { get; } = new();
    }
    public sealed class DeviceUserGroup : NamedObject
    {
        public Composition<Device> Devices { get; } = new();
        public Composition<DeviceUserGroup> Groups { get; } = new();
    }
    public sealed class DeviceItemComposition : Composition<DeviceItem> { }
    public sealed class DeviceItem : NamedObject
    {
        public DeviceItemComposition DeviceItems { get; } = new();
        public Features.SoftwareContainer? Container { get; set; }
        public Exception? ServiceFailure { get; set; }
        public T? GetService<T>() where T : class
        {
            if (ServiceFailure is not null)
                throw ServiceFailure;
            return Container as T;
        }
    }
}

namespace Siemens.Engineering.HW.Features
{
    public sealed class SoftwareContainer
    {
        public object? Software { get; set; }
    }
}

namespace Siemens.Engineering.SW
{
    public sealed class PlcSoftware : NamedObject, IEngineeringServiceProvider
    {
        private readonly Blocks.PlcBlockSystemGroup blockGroup = new();
        public Tags.PlcTagTableGroup TagTableGroup { get; } = new();
        public Exception? BlockGroupFailure { get; set; }
        public Blocks.PlcBlockSystemGroup BlockGroup => BlockGroupFailure is null ? blockGroup : throw BlockGroupFailure;
        public Types.PlcTypeGroup TypeGroup { get; } = new();
        public Units.PlcUnitProvider? UnitProvider { get; set; }
        public ExternalSources.PlcExternalSourceSystemGroup ExternalSourceGroup { get; } = new();
        public Compiler.ICompilable? CompilerService { get; set; }
        public T? GetService<T>() where T : class => UnitProvider as T ?? CompilerService as T;
    }
}

namespace Siemens.Engineering.SW.Tags
{
    public sealed class PlcTagTableGroup : NamedObject
    {
        public Composition<PlcTagTable> TagTables { get; } = new();
        public Composition<PlcTagTableGroup> Groups { get; } = new();
    }
    public sealed class PlcTagTable : NamedObject
    {
        public Composition<PlcTag> Tags { get; } = new();
        public Composition<PlcUserConstant> UserConstants { get; } = new();
        public void Export(FileInfo path, ExportOptions options, DocumentInfoOptions documentInfo)
            => throw new NotSupportedException("Export is outside this offline collision fixture.");
    }
    public sealed class PlcTag : NamedObject
    {
        public string DataTypeName { get; set; } = "Bool";
        public string LogicalAddress { get; set; } = "%I0.0";
        public bool ExternalAccessible { get; set; }
        public bool ExternalVisible { get; set; }
        public bool ExternalWritable { get; set; }
    }
    public sealed class PlcUserConstant : NamedObject
    {
        public string DataTypeName { get; set; } = "Int";
        public object Value { get; set; } = "25";
    }
}

namespace Siemens.Engineering.SW.Blocks
{
    public class PlcBlock : NamedObject, IEngineeringServiceProvider
    {
        public Compiler.ICompilable? CompilerService { get; set; }
        public T? GetService<T>() where T : class => CompilerService as T;
        public int Number { get; set; }
        public string ProgrammingLanguage { get; set; } = "SCL";
        public string? HeaderAuthor { get; set; }
        public string? HeaderFamily { get; set; }
        public string? HeaderName { get; set; }
        public System.Version? HeaderVersion { get; set; }
    }
    public sealed class OB : PlcBlock { }
    public sealed class FB : PlcBlock { }
    public sealed class FC : PlcBlock { }
    public sealed class GlobalDB : PlcBlock { }
    public sealed class InstanceDB : PlcBlock { }
    public sealed class ArrayDB : PlcBlock { }
    public class PlcBlockGroup : NamedObject
    {
        public Composition<PlcBlock> Blocks { get; } = new();
        public Composition<PlcBlockGroup> Groups { get; } = new();
    }
    public sealed class PlcBlockSystemGroup : PlcBlockGroup
    {
        public Composition<PlcSystemBlockGroup> SystemBlockGroups { get; } = new();
    }
    public sealed class PlcBlockUserGroup : PlcBlockGroup { }
    public sealed class PlcSystemBlockGroup : NamedObject
    {
        public Composition<PlcBlock> Blocks { get; } = new();
        public Composition<PlcSystemBlockGroup> Groups { get; } = new();
    }
}

namespace Siemens.Engineering.SW.Types
{
    public sealed class PlcType : NamedObject { }
    public sealed class PlcTypeGroup : NamedObject
    {
        public Composition<PlcType> Types { get; } = new();
        public Composition<PlcTypeGroup> Groups { get; } = new();
    }
}

namespace Siemens.Engineering.SW.ExternalSources
{
    public sealed class PlcExternalSourceSystemGroup { }
}

namespace Siemens.Engineering.SW.Units
{
    public sealed class PlcUnitProvider
    {
        public PlcUnitGroup UnitGroup { get; } = new();
    }
    public sealed class PlcUnitGroup
    {
        public Composition<PlcUnit> Units { get; } = new();
    }
    public sealed class PlcUnit : NamedObject
    {
        public Blocks.PlcBlockGroup BlockGroup { get; } = new();
        public Tags.PlcTagTableGroup TagTableGroup { get; } = new();
        public Types.PlcTypeGroup TypeGroup { get; } = new();
        public ExternalSources.PlcExternalSourceSystemGroup ExternalSourceGroup { get; } = new();
    }
}
