// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.CrossReference
{
    public enum Access : int
    {
        Undefined = 0,
        Read = 1,
        Write = 2,
        RW = 3,
        Unknown = 4,
        Definition = 5,
        Declaration = 6,
        Interface = 7,
        Jump = 8,
        Monitor = 9,
        Modify = 10,
        Force = 11,
        Call = 12,
        UC = 13,
        CC = 14,
        Multiinstance = 15,
        InstanceDB = 16,
        Open = 17,
        Interlock = 18,
        Supervision = 19,
        Actions = 20,
        Transition = 21,
        ReadAndSymbol = 22,
        WriteAndSymbol = 23,
        ReadWriteAndSymbol = 24,
        InstanceAndSymbol = 25,
        MultiinstanceAndSymbol = 26,
        ProDiagSupervision = 27,
        DefaultValue = 28,
        ArrayBoundary = 29,
        StringLength = 30,
        TypeAlarm = 31,
        InstanceAlarm = 32,
        Parameterinstance = 33,
        ParameterinstanceAndSymbol = 34,
        CreateReference = 35,
        CreateReferenceAndSymbol = 36,
    }
    public enum CrossReferenceFilter : int
    {
        AllObjects = 0,
        ObjectsWithReferences = 1,
        ObjectsWithoutReferences = 2,
        UnusedObjects = 3,
    }
    public abstract class CrossReferenceResult : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.CrossReference.SourceObjectComposition Sources { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class CrossReferenceService : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        public global::Siemens.Engineering.CrossReference.CrossReferenceResult GetCrossReferences(global::Siemens.Engineering.CrossReference.CrossReferenceFilter filterType) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class Location : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.CrossReference.Access Access { get => throw new global::System.NotSupportedException(); }
        public string Address { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public string ReferenceLocation { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.CrossReference.ReferenceType ReferenceType { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.IEngineeringObject ReferencedAs { get => throw new global::System.NotSupportedException(); }
        public string ReferencedAsName { get => throw new global::System.NotSupportedException(); }
        public string TypeName { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LocationComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.CrossReference.Location>
    {
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.CrossReference.Location> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class ReferenceObject : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.CrossReference.LocationComposition Locations { get => throw new global::System.NotSupportedException(); }
        public string Address { get => throw new global::System.NotSupportedException(); }
        public string Device { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public string Path { get => throw new global::System.NotSupportedException(); }
        public string TypeName { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class ReferenceObjectComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.CrossReference.ReferenceObject>
    {
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.CrossReference.ReferenceObject> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public enum ReferenceType : int
    {
        Uses = 0,
        UsedBy = 1,
        Undefined = 2,
        TypeInstance = 3,
        InstanceType = 4,
        Assigns = 5,
        MemberGroup = 6,
        GroupMember = 7,
        Defines = 8,
        DefinedBy = 9,
        OverlapsWith = 10,
        Scope = 11,
        Unknown = 12,
    }
    public abstract class SourceObject : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.CrossReference.SourceObjectComposition Children { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.CrossReference.ReferenceObjectComposition References { get => throw new global::System.NotSupportedException(); }
        public string Address { get => throw new global::System.NotSupportedException(); }
        public string Device { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public string Path { get => throw new global::System.NotSupportedException(); }
        public string TypeName { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class SourceObjectComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.CrossReference.SourceObject>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.CrossReference.SourceObject> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
