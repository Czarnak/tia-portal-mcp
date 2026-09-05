// Offline boundary doubles for the source-linked safety reader. These model only the
// Siemens object access it consumes; no Openness assemblies or processes are loaded.
using System.Collections;

namespace Siemens.Engineering
{
    internal abstract class NamedObject
    {
        public string Name { get; set; } = string.Empty;
    }

    internal class Composition<T> : IEnumerable<T> where T : NamedObject
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

    internal sealed class Project
    {
        public Composition<HW.Device> Devices { get; } = new();
        public Composition<HW.DeviceUserGroup> DeviceGroups { get; } = new();
    }

    internal enum ExportOptions { None }
    internal enum DocumentInfoOptions { None }
}

namespace Siemens.Engineering.HW
{
    internal sealed class Device : NamedObject
    {
        public DeviceItemComposition DeviceItems { get; } = new();
    }
    internal sealed class DeviceUserGroup : NamedObject
    {
        public Composition<Device> Devices { get; } = new();
        public Composition<DeviceUserGroup> Groups { get; } = new();
    }
    internal sealed class DeviceItemComposition : Composition<DeviceItem> { }
    internal sealed class DeviceItem : NamedObject
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
    internal sealed class SoftwareContainer
    {
        public object? Software { get; set; }
    }
}

namespace Siemens.Engineering.SW
{
    internal sealed class PlcSoftware : NamedObject
    {
        private readonly Blocks.PlcBlockSystemGroup blockGroup = new();
        public Tags.PlcTagTableGroup TagTableGroup { get; } = new();
        public Exception? BlockGroupFailure { get; set; }
        public Blocks.PlcBlockSystemGroup BlockGroup => BlockGroupFailure is null ? blockGroup : throw BlockGroupFailure;
    }
}

namespace Siemens.Engineering.SW.Tags
{
    internal sealed class PlcTagTableGroup : NamedObject
    {
        public Composition<PlcTagTable> TagTables { get; } = new();
        public Composition<PlcTagTableGroup> Groups { get; } = new();
    }
    internal sealed class PlcTagTable : NamedObject
    {
        public Composition<PlcTag> Tags { get; } = new();
        public Composition<PlcUserConstant> UserConstants { get; } = new();
        public void Export(FileInfo path, ExportOptions options, DocumentInfoOptions documentInfo)
            => throw new NotSupportedException("Export is outside this offline collision fixture.");
    }
    internal sealed class PlcTag : NamedObject
    {
        public string DataTypeName { get; set; } = "Bool";
        public string LogicalAddress { get; set; } = "%I0.0";
        public bool ExternalAccessible { get; set; }
        public bool ExternalVisible { get; set; }
        public bool ExternalWritable { get; set; }
    }
    internal sealed class PlcUserConstant : NamedObject
    {
        public string DataTypeName { get; set; } = "Int";
        public object Value { get; set; } = "25";
    }
}

namespace Siemens.Engineering.SW.Blocks
{
    internal sealed class PlcBlock : NamedObject { }
    internal class PlcBlockGroup : NamedObject
    {
        public Composition<PlcBlock> Blocks { get; } = new();
        public Composition<PlcBlockGroup> Groups { get; } = new();
    }
    internal sealed class PlcBlockSystemGroup : PlcBlockGroup
    {
        public Composition<PlcSystemBlockGroup> SystemBlockGroups { get; } = new();
    }
    internal sealed class PlcSystemBlockGroup : NamedObject
    {
        public Composition<PlcBlock> Blocks { get; } = new();
        public Composition<PlcSystemBlockGroup> Groups { get; } = new();
    }
}
