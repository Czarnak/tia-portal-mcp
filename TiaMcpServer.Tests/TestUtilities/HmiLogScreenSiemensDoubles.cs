// Offline boundary doubles for the hmi_read log and screen readers. They declare the same members as
// reference-stubs/Siemens.Engineering.WinCCUnified/{Logging,Screens}.cs and add test hooks; no Openness
// assembly is loaded. Keep this file in step with the stubs. HmiButton and HmiGraphicView exist only here:
// the readers never name them, tests use them to prove the CLR short type name is reported.
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Base;

namespace Siemens.Engineering.HmiUnified
{
    public sealed partial class HmiSoftware
    {
        public HmiLogging.HmiDataLogComposition DataLogs { get; } = new();
        public HmiLogging.HmiAlarmLogComposition AlarmLogs { get; } = new();
        public HmiLogging.HmiAuditTrailComposition AuditTrails { get; } = new();
        public UI.Screens.HmiScreenComposition Screens { get; } = new();
        public UI.ScreenGroup.HmiScreenGroupComposition ScreenGroups { get; } = new();
    }
}

namespace Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon
{
    public enum DeviceNode { None, Off, Default, Local, SDX51, USBX61, USBX62 }
    public enum HmiBackupMode { NoBackup, PrimaryPath }

    public abstract class DurationBase : PropertyBagNamed
    {
        public uint Days { get => Get<uint>(); set => Set(value); }
        public uint Hours { get => Get<uint>(); set => Set(value); }
        public uint Minutes { get => Get<uint>(); set => Set(value); }
        public uint Seconds { get => Get<uint>(); set => Set(value); }
    }

    public sealed class LogDuration : DurationBase { }
    public sealed class SegmentDuration : DurationBase { }

    public sealed class LogSettings : PropertyBagNamed
    {
        public uint LogMaxSize { get => Get<uint>(); set => Set(value); }
        public LogDuration LogTimePeriod { get => Get<LogDuration>(); set => Set(value); }
        public DeviceNode StorageDevice { get => Get<DeviceNode>(); set => Set(value); }
        public string StorageFolder { get => Get<string>(); set => Set(value); }
    }

    public sealed class LogSegment : PropertyBagNamed
    {
        public uint SegmentMaxSize { get => Get<uint>(); set => Set(value); }
        public DateTime SegmentStartTime { get => Get<DateTime>(); set => Set(value); }
        public SegmentDuration SegmentTimePeriod { get => Get<SegmentDuration>(); set => Set(value); }
    }

    public sealed class LogBackup : PropertyBagNamed
    {
        public HmiBackupMode BackupMode { get => Get<HmiBackupMode>(); set => Set(value); }
        public string PrimaryPath { get => Get<string>(); set => Set(value); }
    }

    public abstract class LoggingBase : PropertyBagNamed
    {
        public LogBackup Backup { get => Get<LogBackup>(); set => Set(value); }
        public LogSegment Segment { get => Get<LogSegment>(); set => Set(value); }
        public LogSettings Settings { get => Get<LogSettings>(); set => Set(value); }
    }
}

namespace Siemens.Engineering.HmiUnified.HmiLogging
{
    public sealed class HmiDataLog : LoggingBase { }
    public sealed class HmiDataLogComposition : Composition<HmiDataLog> { }
    public sealed class HmiAlarmLog : LoggingBase { }
    public sealed class HmiAlarmLogComposition : Composition<HmiAlarmLog> { }
    public sealed class HmiAuditTrail : LoggingBase { }
    public sealed class HmiAuditTrailComposition : Composition<HmiAuditTrail> { }
}

namespace Siemens.Engineering.HmiUnified.UI.Base
{
    public abstract class HmiScreenItemBase : PropertyBagNamed
    {
        public bool Enabled { get => Get<bool>(); set => Set(value); }
        public bool Visible { get => Get<bool>(); set => Set(value); }
    }

    /// <summary>An item with box geometry, like the real concrete types that implement IHmiBoxFeature.</summary>
    public abstract class BoxItem : HmiScreenItemBase, Features.IHmiBoxFeature
    {
        public int Left { get => Get<int>(); set => Set(value); }
        public int Top { get => Get<int>(); set => Set(value); }
        public uint Width { get => Get<uint>(); set => Set(value); }
        public uint Height { get => Get<uint>(); set => Set(value); }

        public BoxItem At(int left, int top, uint width, uint height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            return this;
        }
    }

    public abstract class HmiContainerBase : HmiScreenItemBase
    {
        public string ContainedType { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiScreenItemBaseComposition : Composition<HmiScreenItemBase> { }
}

namespace Siemens.Engineering.HmiUnified.UI.Controls
{
    public sealed class HmiFaceplateContainer : HmiContainerBase
    {
        private readonly Parts.HmiFaceplateInterfaceComposition interfaceBindings = new();
        public Parts.HmiFaceplateInterfaceComposition Interface { get { Check(nameof(Interface)); return interfaceBindings; } }
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Parts
{
    public sealed class HmiFaceplateInterface : PropertyBagNamed
    {
        public string PropertyName { get => Get<string>(); set => Set(value); }
        public object Value { get => Get<object>(); set => Set(value); }
    }

    public sealed class HmiFaceplateInterfaceComposition : Composition<HmiFaceplateInterface> { }
}

namespace Siemens.Engineering.HmiUnified.UI.Widgets
{
    public sealed class HmiButton : BoxItem { }
}

namespace Siemens.Engineering.HmiUnified.UI.Features
{
    public interface IHmiBoxFeature
    {
        uint Height { get; }
        int Left { get; }
        int Top { get; }
        uint Width { get; }
    }
}

namespace Siemens.Engineering.HmiUnified.UI.Shapes
{
    /// <summary>A shape without box geometry (real circles expose a centre and radius instead).</summary>
    public sealed class HmiCircle : HmiScreenItemBase { }

    public sealed class HmiGraphicView : BoxItem { }
}

namespace Siemens.Engineering.HmiUnified.UI.Screens
{
    public sealed class HmiScreenWindow : HmiScreenItemBase
    {
        public string Screen { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiScreen : PropertyBagNamed
    {
        private readonly HmiScreenItemBaseComposition screenItems = new();
        public HmiScreenItemBaseComposition ScreenItems { get { Check(nameof(ScreenItems)); return screenItems; } }
        public MultilingualText DisplayName { get => Get<MultilingualText>(); set => Set(value); }
        public ushort ScreenNumber { get => Get<ushort>(); set => Set(value); }
        public uint Width { get => Get<uint>(); set => Set(value); }
        public uint Height { get => Get<uint>(); set => Set(value); }
    }

    public sealed class HmiScreenComposition : Composition<HmiScreen> { }
}

namespace Siemens.Engineering.HmiUnified.UI.ScreenGroup
{
    public sealed class HmiScreenGroup : Common.HmiGroupBase
    {
        public HmiScreenGroupComposition Groups { get; } = new();
        public Screens.HmiScreenComposition Screens { get; } = new();
    }

    public sealed class HmiScreenGroupComposition : Composition<HmiScreenGroup> { }
}
