using TiaMcpServer.Contracts.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciPathPolicyTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _repositoryRoot;

    public VciPathPolicyTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "tia-mcp-vci-path-tests", Guid.NewGuid().ToString("N"));
        _repositoryRoot = Path.Combine(_testRoot, "repository");
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Fact]
    public void ValidateRoot_AcceptsANonexistentChildOfAnExistingOrdinaryParent()
    {
        var candidate = Path.Combine(_testRoot, "workspace-run");

        var result = ValidateRoot(candidate);

        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(candidate), result.CanonicalPath);
        Assert.False(Directory.Exists(candidate));
    }

    [Theory]
    [InlineData("workspace_root_is_drive_root")]
    [InlineData("workspace_root_is_user_profile")]
    public void ValidateRoot_RejectsProtectedRootsCaseInsensitively(string expectedCategory)
    {
        var candidate = expectedCategory switch
        {
            "workspace_root_is_drive_root" => Path.GetPathRoot(_testRoot)!,
            "workspace_root_is_user_profile" => ToggleCase(_testRoot),
            _ => throw new InvalidOperationException(),
        };

        var result = VciPathPolicy.ValidateRoot(
            candidate,
            _testRoot,
            _ => false);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCategory, result.Reason);
    }


    [Fact]
    public void ValidateRoot_RejectsMissingParent()
    {
        var candidate = Path.Combine(_testRoot, "missing-parent", "workspace-run");

        var result = ValidateRoot(candidate);

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_parent_missing", result.Reason);
    }

    [Fact]
    public void ValidateRoot_RejectsRawParentTraversalEvenWhenCanonicalPathWouldBeContained()
    {
        var candidate = Path.Combine(_testRoot, "unused", "..", "workspace-run");

        var result = ValidateRoot(candidate);

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_traversal", result.Reason);
    }

    [Theory]
    [InlineData("\\\\server\\share\\run")]
    [InlineData("\\\\?\\C:\\run")]
    [InlineData("C:\\run:stream")]
    public void ValidateRoot_RejectsUnsupportedWindowsPathSyntax(string candidate)
    {
        var result = ValidateRoot(candidate);

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_unsupported_path_syntax", result.Reason);
    }

    [Fact]
    public void ValidateRoot_RejectsAnExistingReparseAncestor()
    {
        var parent = Path.Combine(_testRoot, "reparse-parent");
        Directory.CreateDirectory(parent);
        var candidate = Path.Combine(parent, "workspace-run");

        var result = VciPathPolicy.ValidateRoot(
            candidate,
            _testRoot,
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase));

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_reparse_ancestor", result.Reason);
    }

    [Fact]
    public void ResolveRelativeDirectory_AcceptsAContainedExistingDirectoryWithCaseOnlyRootDifference()
    {
        var workspace = CreateWorkspace();
        var nested = Path.Combine(workspace, "nested");
        Directory.CreateDirectory(nested);

        var result = VciPathPolicy.ResolveRelativeDirectory(ToggleCase(workspace), "nested");

        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(nested), result.CanonicalPath, ignoreCase: true);
    }

    [Theory]
    [InlineData("..\\outside", "relative_path_traversal")]
    [InlineData("C:\\outside", "relative_path_must_be_relative")]
    [InlineData("\\\\server\\share", "relative_path_must_be_relative")]
    [InlineData("folder:stream", "relative_path_alternate_data_stream")]
    public void ResolveRelativeDirectory_RejectsEscapesAndSpecialSyntax(string relativePath, string expectedCategory)
    {
        var result = VciPathPolicy.ResolveRelativeDirectory(CreateWorkspace(), relativePath);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCategory, result.Reason);
    }

    [Fact]
    public void ResolveRelativeDirectory_RejectsAFileValuedDirectory()
    {
        var workspace = CreateWorkspace();
        File.WriteAllText(Path.Combine(workspace, "file"), "content");

        var fileResult = VciPathPolicy.ResolveRelativeDirectory(workspace, "file");

        Assert.Equal("relative_directory_is_file", fileResult.Reason);
    }

    [Fact]
    public void ResolveRelativeDirectory_RejectsAReparseEscapeBeforeResolvingChildren()
    {
        var workspace = CreateWorkspace();
        var reparse = Path.Combine(workspace, "link");
        Directory.CreateDirectory(reparse);

        var result = VciPathPolicy.ResolveRelativeDirectory(
            workspace,
            "link",
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(reparse), StringComparison.OrdinalIgnoreCase));

        Assert.False(result.IsValid);
        Assert.Equal("relative_path_reparse_point", result.Reason);
    }

    [Fact]
    public void ExistingRootAndGitRoot_AreAccepted()
    {
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, ".git"));
        var marker = Path.Combine(_repositoryRoot, "keep.txt");
        File.WriteAllText(marker, "keep");
        Assert.True(ValidateRoot(CreateWorkspace()).IsValid);
        Assert.True(ValidateRoot(_repositoryRoot).IsValid);
        Assert.Equal("keep", File.ReadAllText(marker));
    }

    [Fact]
    public void MissingRelativeDirectory_IsAccepted()
    {
        var workspace = CreateWorkspace();
        var result = VciPathPolicy.ResolveRelativeDirectory(workspace, @"missing\nested");
        Assert.True(result.IsValid);
        Assert.Equal(Path.Combine(workspace, "missing", "nested"), result.CanonicalPath);
        Assert.False(Directory.Exists(result.CanonicalPath));
    }

    [Fact]
    public void FileValuedAncestor_IsRejectedEvenWhenTargetIsMissing()
    {
        var workspace = CreateWorkspace();
        File.WriteAllText(Path.Combine(workspace, "file"), "keep");
        Assert.Equal("relative_directory_is_file", VciPathPolicy.ResolveRelativeDirectory(workspace, @"file\missing").Reason);
        Assert.Equal("workspace_root_is_file", ValidateRoot(Path.Combine(workspace, "file")).Reason);
    }

    [Fact]
    public void ExistingRootItselfIsInspectedForReparsePoint()
    {
        var workspace = CreateWorkspace();
        Assert.Equal("workspace_root_reparse_ancestor", VciPathPolicy.ValidateRoot(workspace, _testRoot,
            path => string.Equals(path, workspace, StringComparison.OrdinalIgnoreCase)).Reason);
    }

    [Theory]
    [InlineData(null, "workspace_root_required")]
    [InlineData("", "workspace_root_required")]
    [InlineData("relative", "workspace_root_must_be_absolute")]
    [InlineData("x", "workspace_root_must_be_absolute")]
    [InlineData("C:relative", "workspace_root_must_be_absolute")]
    [InlineData("\\relative", "workspace_root_must_be_absolute")]
    public void RootMustBeFullyQualified(string? path, string reason)
        => Assert.Equal(reason, VciPathPolicy.ValidateRoot(path, _testRoot, _ => false).Reason);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"nested\child")]
    public void RelativeSyntax_AcceptsOmittedEmptyOrNested(string? relative)
        => Assert.True(VciPathPolicy.ValidateRelativeSyntax(relative).IsValid);

    [Theory]
    [InlineData(".")]
    [InlineData(@"nested\.\child")]
    [InlineData("bad\0name")]
    public void RelativeSyntax_RejectsInvalidSegments(string relative)
        => Assert.Equal("relative_path_invalid", VciPathPolicy.ValidateRelativeSyntax(relative).Reason);

    [Fact]
    public void InspectionFailure_IsRejected()
        => Assert.Equal("path_inspection_failed", VciPathPolicy.ValidateRoot(CreateWorkspace(), _testRoot,
            _ => throw new UnauthorizedAccessException()).Reason);

    [Fact]
    public void DefaultRootOverload_AcceptsOrdinaryDirectory()
        => Assert.True(VciPathPolicy.ValidateRoot(_repositoryRoot).IsValid);

    [Fact]
    public void MissingRootAndRelativeDirectory_AreAcceptedWithSafeAncestors()
    {
        var root = Path.Combine(_testRoot, "missing-root");
        Assert.True(VciPathPolicy.ResolveRelativeDirectory(root, "nested").IsValid);
        Assert.Equal("relative_path_reparse_point", VciPathPolicy.ResolveRelativeDirectory(root, "nested",
            path => string.Equals(path, _testRoot, StringComparison.OrdinalIgnoreCase)).Reason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private VciPathCheck ValidateRoot(string candidate)
        => VciPathPolicy.ValidateRoot(
            candidate,
            _testRoot,
            path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0);

    private string CreateWorkspace()
    {
        var workspace = Path.Combine(_testRoot, "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static string ToggleCase(string value)
        => string.Concat(value.Select(character => char.IsLetter(character)
            ? (char.IsUpper(character) ? char.ToLowerInvariant(character) : char.ToUpperInvariant(character))
            : character));
}
