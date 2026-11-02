// Offline boundary doubles for the hmi_read tag readers. They declare the same members as
// reference-stubs/Siemens.Engineering.WinCCUnified/Tags.cs and add test hooks; no Openness assembly is
// loaded. Keep this file in step with the stub.
using System.Runtime.CompilerServices;

namespace Siemens.Engineering.HmiUnified
{
    public sealed partial class HmiSoftware
    {
        public HmiTags.HmiTagComposition Tags { get; } = new();
        public HmiTags.HmiTagTableComposition TagTables { get; } = new();
        public HmiTags.HmiTagTableGroupComposition TagTableGroups { get; } = new();
        public HmiTags.HmiSystemTagComposition SystemTags { get; } = new();
    }
}

namespace Siemens.Engineering.HmiUnified.Common
{
    public abstract class HmiGroupBase : NamedObject { }

    public sealed class HmiValidationResult
    {
        public string PropertyName { get; set; } = string.Empty;
        private IEnumerable<string>? errors = Array.Empty<string>();
        private IEnumerable<string>? warnings = Array.Empty<string>();
        public Exception? ErrorsFailure { get; set; }
        public Exception? WarningsFailure { get; set; }
        public IEnumerable<string>? Errors { get => ErrorsFailure is null ? errors : throw ErrorsFailure; set => errors = value; }
        public IEnumerable<string>? Warnings { get => WarningsFailure is null ? warnings : throw WarningsFailure; set => warnings = value; }
    }

    public interface IValidator
    {
        IList<HmiValidationResult> Validate();
    }

    /// <summary>A property bag with <c>Validate</c>: returns <c>Results</c>, or throws <c>ValidateFailure</c>, and counts calls.</summary>
    public abstract class ValidatableBag : HmiTags.PropertyBagNamed, IValidator
    {
        public List<HmiValidationResult> Results { get; } = new();
        public Exception? ValidateFailure { get; set; }
        public bool ValidateReturnsNull { get; set; }
        public int ValidateCalls { get; private set; }

        public IList<HmiValidationResult> Validate()
        {
            ValidateCalls++;
            if (ValidateFailure is not null) throw ValidateFailure;
            return ValidateReturnsNull ? null! : Results.ToList();
        }
    }
}

namespace Siemens.Engineering.HmiUnified.HmiTags
{
    /// <summary>A named object whose properties can be made to throw (<c>Failures[name]</c>) and that records reads (<c>Reads</c>).</summary>
    public abstract class PropertyBagNamed : NamedObject
    {
        private readonly Dictionary<string, object?> values = new();
        public Dictionary<string, Exception> Failures { get; } = new();
        public List<string> Reads { get; } = new();

        protected T Get<T>([CallerMemberName] string name = "")
        {
            Check(name);
            return values.TryGetValue(name, out var v) ? (T)v! : default!;
        }

        public List<string> Writes { get; } = new();
        protected void Set(object? value, [CallerMemberName] string name = "") { Writes.Add(name); if (SetFailures.TryGetValue(name, out var failure)) throw failure; values[name] = value; }
        public Dictionary<string, Exception> SetFailures { get; } = new();

        protected void Check(string name)
        {
            Reads.Add(name);
            if (Failures.TryGetValue(name, out var failure)) throw failure;
        }
    }

    public enum HmiAccessMode { None, AbsoluteAccess, SymbolicAccess }
    public enum HmiAcquisitionMode { None, OnDemand, CyclicOnUse, CyclicContinuous }
    public enum HmiSubstituteValueUsage { None, InvalidValue, RangeViolation, InvalidValueOrRangeViolation }
    public enum HmiLimitValueType { None, Constant, Tag }
    public enum HmiThresholdMode { None, Upper, Lower }

    public sealed class HmiTag : Common.ValidatableBag
    {
        public Action? DeleteAction { get; set; }
        public void Delete() => DeleteAction?.Invoke();
        private readonly LoggingTags.HmiLoggingTagComposition loggingTags = new();
        private readonly HmiTagComposition members = new();
        private readonly HmiThresholdComposition thresholds = new();

        public MultilingualText Comment { get => Get<MultilingualText>() ?? comment; set => Set(value); }
        private readonly MultilingualText comment = new MultilingualText().With("en-US", "");
        public MultilingualText DisplayName { get => Get<MultilingualText>() ?? displayName; set => Set(value); }
        private readonly MultilingualText displayName = new MultilingualText().With("en-US", "");
        public string PlcName { get => Get<string>(); set => Set(value); }
        public LoggingTags.HmiLoggingTagComposition LoggingTags { get { Check(nameof(LoggingTags)); return loggingTags; } }
        public HmiTagComposition Members { get { Check(nameof(Members)); return members; } }
        public HmiThresholdComposition Thresholds { get { Check(nameof(Thresholds)); return thresholds; } }
        public HmiAccessMode AccessMode { get => Get<HmiAccessMode>(); set => Set(value); }
        public string AcquisitionCycle { get => Get<string>(); set => Set(value); }
        public HmiAcquisitionMode AcquisitionMode { get => Get<HmiAcquisitionMode>(); set => Set(value); }
        public string Address { get => Get<string>(); set => Set(value); }
        public string Connection { get => Get<string>(); set => Set(value); }
        public string DataType { get => Get<string>(); set => Set(value); }
        public string HmiDataType { get => Get<string>(); set => Set(value); }
        public object HmiEndValue { get => Get<object>(); set => Set(value); }
        public object HmiStartValue { get => Get<object>(); set => Set(value); }
        public UpperRange InitialMaxValue { get => Get<UpperRange>(); set => Set(value); }
        public LowerRange InitialMinValue { get => Get<LowerRange>(); set => Set(value); }
        public object InitialValue { get => Get<object>(); set => Set(value); }
        public bool LinearScaling { get => Get<bool>(); set => Set(value); }
        public bool Persistent { get => Get<bool>(); set => Set(value); }
        public object PlcEndValue { get => Get<object>(); set => Set(value); }
        public object PlcStartValue { get => Get<object>(); set => Set(value); }
        public string PlcTag { get => Get<string>(); set => Set(value); }
        public HmiSubstituteValue SubstituteValue { get => Get<HmiSubstituteValue>(); set => Set(value); }
        public string TagTableName { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiTagComposition : Composition<HmiTag>
    {
        /// <summary>Exact-name lookup that, like Openness, does not enumerate.</summary>
        public Func<string, HmiTag?>? FindOverride { get; set; }
        public Action<HmiTag>? Created { get; set; }
        public new HmiTag? Find(string name) => FindOverride is null ? Items.FirstOrDefault(x => x.Name == name) : FindOverride(name);
        public HmiTag Create(string name, string table)
        {
            if (Items.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) throw new EngineeringException("Tag already exists.");
            var tag = new HmiTag { Name = name, TagTableName = table };
            tag.Writes.Clear();
            tag.DeleteAction = () => Items.Remove(tag);
            Items.Add(tag);
            Created?.Invoke(tag);
            return tag;
        }
    }

    public sealed class HmiTagTable : NamedObject
    {
        public Action? DeleteAction { get; set; }
        public void Delete() => DeleteAction?.Invoke();
        public HmiTagComposition Tags { get; } = new();
    }

    public sealed class HmiTagTableComposition : Composition<HmiTagTable>
    {
        public HmiTagTable Create(string name)
        {
            if (Find(name) is not null) throw new EngineeringException("Table already exists.");
            var table = new HmiTagTable { Name = name };
            table.DeleteAction = () => Items.Remove(table);
            Items.Add(table);
            return table;
        }
    }

    public sealed class HmiTagTableGroup : Common.HmiGroupBase
    {
        public HmiTagTableGroupComposition Groups { get; } = new();
        public HmiTagTableComposition TagTables { get; } = new();
    }

    public sealed class HmiTagTableGroupComposition : Composition<HmiTagTableGroup> { }

    public sealed class HmiSystemTag : PropertyBagNamed
    {
        public string DataType { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiSystemTagComposition : Composition<HmiSystemTag> { }

    public sealed class HmiThreshold : PropertyBagNamed
    {
        public HmiThresholdMode Mode { get => Get<HmiThresholdMode>(); set => Set(value); }
        public object Value { get => Get<object>(); set => Set(value); }
        public HmiLimitValueType ValueType { get => Get<HmiLimitValueType>(); set => Set(value); }
    }

    public sealed class HmiThresholdComposition : Composition<HmiThreshold> { }

    public abstract class Range
    {
        public object Value { get; set; } = string.Empty;
        public HmiLimitValueType ValueType { get; set; }
    }

    public sealed class UpperRange : Range { }

    public sealed class LowerRange : Range { }

    public sealed class HmiSubstituteValue
    {
        public HmiSubstituteValueUsage SubstituteValueUsage { get; set; }
        public object Value { get; set; } = string.Empty;
    }
}

namespace Siemens.Engineering.HmiUnified.LoggingTags
{
    public enum HmiLoggingMode { Undefined, Cyclic, OnDemand, OnChange }
    public enum HmiTriggerMode { None, RisingEdge, FallingEdge, RisingAndFallingEdge }

    public sealed class HmiLoggingTag : Common.ValidatableBag
    {
        public string Cycle { get => Get<string>(); set => Set(value); }
        public string DataLog { get => Get<string>(); set => Set(value); }
        public object HighLimit { get => Get<object>(); set => Set(value); }
        public HmiLoggingMode LoggingMode { get => Get<HmiLoggingMode>(); set => Set(value); }
        public object LowLimit { get => Get<object>(); set => Set(value); }
        public HmiTriggerMode TriggerMode { get => Get<HmiTriggerMode>(); set => Set(value); }
    }

    public sealed class HmiLoggingTagComposition : Composition<HmiLoggingTag> { }
}
