// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.SW.ExternalSources
{
    public enum GenerateBlockOption : int
    {
        None = 0,
    }
    public enum GenerateOptions : int
    {
        None = 0,
        WithDependencies = 1,
    }
    public interface IGenerateSource
    {
    }
    public abstract class PlcExternalSource : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public void Delete() => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.IEngineeringObject> GenerateBlocksFromSource(global::Siemens.Engineering.SW.ExternalSources.GenerateBlockOption generateBlockOption) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.IEngineeringObject> GenerateBlocksFromSource(global::Siemens.Engineering.SW.Types.PlcTypeUserGroup groupName, global::Siemens.Engineering.SW.ExternalSources.GenerateBlockOption generateBlockOption) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.IEngineeringObject> GenerateBlocksFromSource(global::Siemens.Engineering.SW.Blocks.PlcBlockUserGroup groupName, global::Siemens.Engineering.SW.ExternalSources.GenerateBlockOption generateBlockOption) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcExternalSourceComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.SW.ExternalSources.PlcExternalSource>
    {
        public global::Siemens.Engineering.SW.ExternalSources.PlcExternalSource CreateFromFile(string name, string path) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.SW.ExternalSources.PlcExternalSource> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class PlcExternalSourceGroup : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.SW.ExternalSources.PlcExternalSourceComposition ExternalSources { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcExternalSourceSystemGroup : global::Siemens.Engineering.SW.ExternalSources.PlcExternalSourceGroup, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public void GenerateSource(global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.SW.ExternalSources.IGenerateSource> blocks, global::System.IO.FileInfo sourceFile, global::Siemens.Engineering.SW.ExternalSources.GenerateOptions generateOption) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}
