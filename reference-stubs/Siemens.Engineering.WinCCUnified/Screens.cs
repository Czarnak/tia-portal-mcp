// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.HmiUnified
{
    public abstract partial class HmiSoftware
    {
        public global::Siemens.Engineering.HmiUnified.UI.Screens.HmiScreenComposition Screens { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.UI.ScreenGroup.HmiScreenGroupComposition ScreenGroups { get => throw new global::System.NotSupportedException(); }
    }
}
namespace Siemens.Engineering.HmiUnified.UI
{
    public abstract class UIBase : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Base
{
    public abstract class HmiScreenBase : global::Siemens.Engineering.HmiUnified.UI.UIBase
    {
        public global::Siemens.Engineering.MultilingualText DisplayName { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiScreenItemBase : global::Siemens.Engineering.HmiUnified.UI.UIBase
    {
        public bool Enabled { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public bool Visible { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiContainerBase : HmiScreenItemBase
    {
        public string ContainedType { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiScreenItemBaseComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiScreenItemBase>
    {
        public global::System.Collections.Generic.IEnumerator<HmiScreenItemBase> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Controls
{
    public abstract class HmiFaceplateContainer : global::Siemens.Engineering.HmiUnified.UI.Base.HmiContainerBase
    {
        public global::Siemens.Engineering.HmiUnified.UI.Parts.HmiFaceplateInterfaceComposition Interface { get => throw new global::System.NotSupportedException(); }
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Parts
{
    public abstract class HmiFaceplateInterface : global::Siemens.Engineering.HmiUnified.UI.UIBase
    {
        public string PropertyName { get => throw new global::System.NotSupportedException(); }
        public object Value { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiFaceplateInterfaceComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiFaceplateInterface>
    {
        public global::System.Collections.Generic.IEnumerator<HmiFaceplateInterface> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
namespace Siemens.Engineering.HmiUnified.UI.Screens
{
    public abstract class HmiScreen : global::Siemens.Engineering.HmiUnified.UI.Base.HmiScreenBase
    {
        public uint Height { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.UI.Base.HmiScreenItemBaseComposition ScreenItems { get => throw new global::System.NotSupportedException(); }
        public ushort ScreenNumber { get => throw new global::System.NotSupportedException(); }
        public uint Width { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiScreenComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiScreen>
    {
        public global::System.Collections.Generic.IEnumerator<HmiScreen> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class HmiScreenWindow : global::Siemens.Engineering.HmiUnified.UI.Base.HmiScreenItemBase
    {
        public string Screen { get => throw new global::System.NotSupportedException(); }
    }
}
namespace Siemens.Engineering.HmiUnified.UI.ScreenGroup
{
    public abstract class HmiScreenGroup : global::Siemens.Engineering.HmiUnified.Common.HmiGroupBase
    {
        public HmiScreenGroupComposition Groups { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.UI.Screens.HmiScreenComposition Screens { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiScreenGroupComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiScreenGroup>
    {
        public global::System.Collections.Generic.IEnumerator<HmiScreenGroup> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
