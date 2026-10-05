// Offline boundary doubles for source-linked readers and the project snapshot walker.
// These model object access only; no Openness assemblies or processes are loaded.
using System.Collections;

namespace Siemens.Engineering
{
    public abstract class NamedObject : IEngineeringServiceProvider, IEngineeringObject
    {
        public object GetAttribute(string attributeName) => attributeName == "Name"
            ? Name
            : throw new EngineeringException($"Attribute '{attributeName}' is not modeled.");
        private string name = string.Empty;
        public Exception? NameFailure { get; set; }
        public string Name
        {
            get => NameFailure is null ? name : throw NameFailure;
            set => name = value;
        }
        public CrossReference.CrossReferenceService? CrossReferenceService { get; set; }
        public Exception? CrossReferenceServiceFailure { get; set; }
        public int CrossReferenceServiceRequests { get; private set; }
        public virtual T? GetService<T>() where T : class
        {
            if (typeof(T) != typeof(CrossReference.CrossReferenceService)) return null;
            CrossReferenceServiceRequests++;
            if (CrossReferenceServiceFailure is not null) throw CrossReferenceServiceFailure;
            return CrossReferenceService as T;
        }
    }

    public class Composition<T> : IEnumerable<T> where T : NamedObject
    {
        public List<T> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public int YieldedItemCount { get; private set; }
        public T? Find(string name) => this.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in Items)
            {
                YieldedItemCount++;
                yield return item;
            }
            if (EnumerationFailure is not null)
                throw EnumerationFailure;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class Project
    {
        private HW.DeviceSystemGroup ungroupedDevicesGroup = new();
        public Composition<HW.Device> Devices { get; } = new();
        public Composition<HW.DeviceUserGroup> DeviceGroups { get; } = new();
        public Exception? UngroupedDevicesGroupFailure { get; set; }
        public HW.DeviceSystemGroup UngroupedDevicesGroup
        {
            get => UngroupedDevicesGroupFailure is null ? ungroupedDevicesGroup : throw UngroupedDevicesGroupFailure;
            set => ungroupedDevicesGroup = value;
        }
    }

    public enum ExportOptions { None }
    public enum DocumentInfoOptions { None }
    public class EngineeringException : Exception
    {
        public EngineeringException() { }
        public EngineeringException(string message) : base(message) { }
    }

    // Inheritance deliberately exercises exclusion before the broad EngineeringException check.
    public sealed class NonRecoverableException : EngineeringException
    {
        public NonRecoverableException(string message) : base(message) { }
    }

    public interface IEngineeringServiceProvider
    {
        T? GetService<T>() where T : class;
    }

    public interface IEngineeringObject
    {
        object GetAttribute(string name);
    }
}

namespace Siemens.Engineering.CrossReference
{
    public enum CrossReferenceFilter { AllObjects, ObjectsWithReferences, ObjectsWithoutReferences, UnusedObjects }
    public sealed class CrossReferenceService
    {
        public CrossReferenceResult Result { get; set; } = new();
        public Exception? Failure { get; set; }
        public List<CrossReferenceFilter> Queries { get; } = new();
        public CrossReferenceResult GetCrossReferences(CrossReferenceFilter filter)
        {
            Queries.Add(filter);
            if (Failure is not null) throw Failure;
            return Result;
        }
    }
    public sealed class CrossReferenceResult
    {
        public Composition<SourceObject> Sources { get; } = new();
    }
    public class CrossReferenceObject : NamedObject
    {
        private string typeName = string.Empty;
        public Exception? TypeNameFailure { get; set; }
        public string TypeName
        {
            get => TypeNameFailure is null ? typeName : throw TypeNameFailure;
            set => typeName = value;
        }
        public string Path { get; set; } = string.Empty;
        public string Device { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }
    public sealed class SourceObject : CrossReferenceObject
    {
        public Composition<ReferenceObject> References { get; } = new();
        public Composition<SourceObject> Children { get; } = new();
    }
    public sealed class ReferenceObject : CrossReferenceObject
    {
        public Composition<Location> Locations { get; } = new();
    }
    public sealed class Location : CrossReferenceObject
    {
        public Access Access { get; set; }
        public ReferenceType ReferenceType { get; set; }
        public string ReferenceLocation { get; set; } = string.Empty;
        public IEngineeringObject? ReferencedAs { get; set; }
        public string ReferencedAsName { get; set; } = string.Empty;
    }

    // Members and values match the V21 metadata and the reference stub.
    public enum Access
    {
        Undefined, Read, Write, RW, Unknown, Definition, Declaration, Interface, Jump, Monitor, Modify, Force,
        Call, UC, CC, Multiinstance, InstanceDB, Open, Interlock, Supervision, Actions, Transition, ReadAndSymbol,
        WriteAndSymbol, ReadWriteAndSymbol, InstanceAndSymbol, MultiinstanceAndSymbol, ProDiagSupervision,
        DefaultValue, ArrayBoundary, StringLength, TypeAlarm, InstanceAlarm, Parameterinstance,
        ParameterinstanceAndSymbol, CreateReference, CreateReferenceAndSymbol
    }

    public enum ReferenceType
    {
        Uses, UsedBy, Undefined, TypeInstance, InstanceType, Assigns, MemberGroup, GroupMember, Defines,
        DefinedBy, OverlapsWith, Scope, Unknown
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
        private readonly List<CompilerResultMessage> messages = new();
        public CompilerResultState State { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public Exception? MessagesFailure { get; set; }
        public List<CompilerResultMessage> Messages => MessagesFailure is null ? messages : throw MessagesFailure;
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
    public sealed class DeviceSystemGroup : NamedObject
    {
        private Composition<Device> devices = new();
        public Exception? DevicesFailure { get; set; }
        public Composition<Device> Devices
        {
            get => DevicesFailure is null ? devices : throw DevicesFailure;
            set => devices = value;
        }
    }
    public sealed class DeviceItemComposition : Composition<DeviceItem> { }
    public sealed class DeviceItem : NamedObject
    {
        public DeviceItemComposition DeviceItems { get; } = new();
        public Features.SoftwareContainer? Container { get; set; }
        public Exception? ServiceFailure { get; set; }
        public override T? GetService<T>() where T : class
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
        public override T? GetService<T>() where T : class => UnitProvider as T ?? CompilerService as T ?? base.GetService<T>();
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
        public Composition<PlcSystemConstant> SystemConstants { get; } = new();
        public bool IsDefault { get; set; }
        public void Export(FileInfo path, ExportOptions options, DocumentInfoOptions documentInfo)
            => throw new NotSupportedException("Export is outside this offline collision fixture.");
    }
    public sealed class PlcTag : NamedObject
    {
        public string DataTypeName { get; set; } = "Bool";
        public string LogicalAddress { get; set; } = "%I0.0";
        private bool externalAccessible, externalVisible, externalWritable;
        public Exception? ExternalAccessibleFailure { get; set; }
        public Exception? ExternalVisibleFailure { get; set; }
        public Exception? ExternalWritableFailure { get; set; }
        public bool ExternalAccessible
        {
            get => ExternalAccessibleFailure is null ? externalAccessible : throw ExternalAccessibleFailure;
            set => externalAccessible = value;
        }
        public bool ExternalVisible
        {
            get => ExternalVisibleFailure is null ? externalVisible : throw ExternalVisibleFailure;
            set => externalVisible = value;
        }
        public bool ExternalWritable
        {
            get => ExternalWritableFailure is null ? externalWritable : throw ExternalWritableFailure;
            set => externalWritable = value;
        }
    }
    public sealed class PlcSystemConstant : NamedObject { }
    public sealed class PlcUserConstant : NamedObject
    {
        public string DataTypeName { get; set; } = "Int";
        private object value = "25";
        public Exception? ValueFailure { get; set; }
        public object Value
        {
            get => ValueFailure is null ? value : throw ValueFailure;
            set => this.value = value;
        }
    }
}

namespace Siemens.Engineering.SW.Blocks
{
    public class PlcBlock : NamedObject, IEngineeringServiceProvider
    {
        public Compiler.ICompilable? CompilerService { get; set; }
        public override T? GetService<T>() where T : class => CompilerService as T ?? base.GetService<T>();
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
        public Blocks.PlcBlockSystemGroup BlockGroup { get; } = new();
        public Tags.PlcTagTableGroup TagTableGroup { get; } = new();
        public Types.PlcTypeGroup TypeGroup { get; } = new();
        public ExternalSources.PlcExternalSourceSystemGroup ExternalSourceGroup { get; } = new();
    }
}
