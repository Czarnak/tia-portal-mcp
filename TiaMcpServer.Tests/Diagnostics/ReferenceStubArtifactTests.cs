using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class ReferenceStubArtifactTests
{
    [Theory]
    [InlineData("Siemens.Engineering.Base")]
    [InlineData("Siemens.Engineering.Step7")]
    public void TrackedReferencesHaveIdentityAndCurrentSourceProvenance(string name)
    {
        using var stream = File.OpenRead(Path.Combine(RepositoryRoot, "ref", name + ".dll"));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        Assert.Equal(name, reader.GetString(assembly.Name));
        Assert.Equal(new Version(21, 0, 0, 0), assembly.Version);
        var token = SHA1.HashData(reader.GetBlobBytes(assembly.PublicKey))[^8..].Reverse().ToArray();
        Assert.Equal("29bfe5fdf4ba5d3b", Convert.ToHexString(token).ToLowerInvariant());
        var metadata = ReadAssemblyMetadata(reader);
        Assert.Equal("tia-portal-mcp", metadata["StubOrigin"]);
        Assert.Equal(SourceHash(RepositoryRoot), metadata["StubSourceHash"]);
        Assert.Contains("ReferenceAssemblyAttribute", AssemblyAttributeNames(reader));
    }

    [Fact]
    public void BaseReferenceExposesLockedPublicMultiuserFoundation()
    {
        using var stream = File.OpenRead(Path.Combine(RepositoryRoot, "ref", "Siemens.Engineering.Base.dll"));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.ToDictionary(h => FullName(reader, h));
        string[] required = ["Siemens.Engineering.ProjectBase", "Siemens.Engineering.Project", "Siemens.Engineering.TiaPortal",
            "Siemens.Engineering.Multiuser.MultiuserProject", "Siemens.Engineering.Multiuser.LocalSession",
            "Siemens.Engineering.Multiuser.LocalSessionComposition", "Siemens.Engineering.Multiuser.LocalSessionInfo",
            "Siemens.Engineering.Multiuser.MarkingService", "Siemens.Engineering.Multiuser.ProjectServer",
            "Siemens.Engineering.Multiuser.ProjectServerComposition"];
        foreach (var name in required)
        {
            Assert.True(types.TryGetValue(name, out var handle), $"Missing public foundation type {name}");
            Assert.Equal(TypeAttributes.Public, reader.GetTypeDefinition(handle).Attributes & TypeAttributes.VisibilityMask);
        }
        foreach (var name in new[] { "Siemens.Engineering.Project", "Siemens.Engineering.Multiuser.MultiuserProject" })
            Assert.Equal("Siemens.Engineering.ProjectBase", FullName(reader, reader.GetTypeDefinition(types[name]).BaseType));
        AssertMembers("Siemens.Engineering.TiaPortal", "get_LocalSessions", "get_ProjectServers");
        AssertMembers("Siemens.Engineering.Multiuser.LocalSessionComposition", "Open", "OpenServerProject");
        AssertMembers("Siemens.Engineering.Multiuser.LocalSession", "get_Project", "get_MarkingService", "Save", "Close", "CloseAndCommit", "IsUptoDate");
        AssertMembers("Siemens.Engineering.Multiuser.LocalSessionInfo", "get_ProjectFileInfo", "get_SessionId");
        void AssertMembers(string name, params string[] expected)
        {
            var members = reader.GetTypeDefinition(types[name]).GetMethods().Select(h => reader.GetString(reader.GetMethodDefinition(h).Name)).ToArray();
            foreach (var member in expected) Assert.Contains(member, members);
        }
    }

    [Fact]
    public void BaseReferenceExposesPublicInstanceReadOnlyIoSystemSubnet()
    {
        using var stream = File.OpenRead(Path.Combine(RepositoryRoot, "ref", "Siemens.Engineering.Base.dll"));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var typeHandle = Assert.Single(reader.TypeDefinitions,
            h => FullName(reader, h) == "Siemens.Engineering.HW.IoSystem");
        var type = reader.GetTypeDefinition(typeHandle);
        var propertyHandle = Assert.Single(type.GetProperties(),
            h => reader.GetString(reader.GetPropertyDefinition(h).Name) == "Subnet");
        var property = reader.GetPropertyDefinition(propertyHandle);
        var accessors = property.GetAccessors();
        Assert.False(accessors.Getter.IsNil);
        Assert.True(accessors.Setter.IsNil);
        Assert.Empty(accessors.Others);

        var getter = reader.GetMethodDefinition(accessors.Getter);
        Assert.Equal("get_Subnet", reader.GetString(getter.Name));
        Assert.Equal(MethodAttributes.Public, getter.Attributes & MethodAttributes.MemberAccessMask);
        Assert.Equal((MethodAttributes)0, getter.Attributes & MethodAttributes.Static);
        Assert.NotEqual((MethodAttributes)0, getter.Attributes & MethodAttributes.SpecialName);
        AssertSubnetSignature(getter.Signature, SignatureKind.Method);
        AssertSubnetSignature(property.Signature, SignatureKind.Property);

        void AssertSubnetSignature(BlobHandle signature, SignatureKind kind)
        {
            var blob = reader.GetBlobReader(signature);
            var header = blob.ReadSignatureHeader();
            Assert.Equal(kind, header.Kind);
            Assert.True(header.IsInstance);
            Assert.False(header.IsGeneric);
            Assert.Equal(0, blob.ReadCompressedInteger());
            Assert.Equal(SignatureTypeCode.TypeHandle, blob.ReadSignatureTypeCode());
            Assert.Equal("Siemens.Engineering.HW.Subnet", FullName(reader, blob.ReadTypeHandle()));
            Assert.Equal(0, blob.RemainingBytes);
        }
    }
    internal static string RepositoryRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    // Canonical framing: UTF-8 slash-relative path, LF, decimal byte length, LF, exact bytes, LF.
    internal static string[] SourceFiles(string root) => new[] { "Directory.Build.props", "reference-stubs/Directory.Build.props", "reference-stubs/Siemens.Engineering.PublicKey.snk" }
        .Concat(new[] { "Siemens.Engineering.Base", "Siemens.Engineering.Step7" }.SelectMany(name =>
            Directory.EnumerateFiles(Path.Combine(root, "reference-stubs", name), "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".csproj", StringComparison.Ordinal))
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                .Where(p => !p.Split('/').Any(segment => segment is "bin" or "obj" or "artifacts"))))
        .Order(StringComparer.Ordinal).ToArray();

    internal static string SourceHash(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in SourceFiles(root))
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, relative));
            hash.AppendData(Encoding.UTF8.GetBytes(relative + "\n" + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"));
            hash.AppendData(bytes);
            hash.AppendData([10]);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string FullName(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Namespace) + "." + reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
        HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Namespace) + "." + reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name),
        _ => throw new InvalidOperationException("Unexpected type handle " + handle.Kind)
    };

    private static string AttributeName(MetadataReader reader, CustomAttribute attribute)
    {
        var parent = attribute.Constructor.Kind == HandleKind.MemberReference
            ? reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
            : reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
        return FullName(reader, parent).Split('.').Last();
    }

    private static IEnumerable<string> AssemblyAttributeNames(MetadataReader reader) => reader.GetAssemblyDefinition().GetCustomAttributes()
        .Select(h => AttributeName(reader, reader.GetCustomAttribute(h)));

    private static Dictionary<string, string> ReadAssemblyMetadata(MetadataReader reader)
    {
        var result = new Dictionary<string, string>();
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (AttributeName(reader, attribute) != "AssemblyMetadataAttribute") continue;
            var blob = reader.GetBlobReader(attribute.Value);
            Assert.Equal(1, blob.ReadUInt16());
            result.Add(blob.ReadSerializedString()!, blob.ReadSerializedString()!);
        }
        return result;
    }
}
