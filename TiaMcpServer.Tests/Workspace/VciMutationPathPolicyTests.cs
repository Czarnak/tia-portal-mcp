using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

public sealed class VciMutationPathPolicyTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _repositoryRoot;
    private readonly string _projectDirectory;
    private readonly string _projectPath;

    public VciMutationPathPolicyTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "tia-mcp-vci-path-tests", Guid.NewGuid().ToString("N"));
        _repositoryRoot = Path.Combine(_testRoot, "repository");
        _projectDirectory = Path.Combine(_testRoot, "projects");
        _projectPath = Path.Combine(_projectDirectory, "fixture.ap21");
        Directory.CreateDirectory(_repositoryRoot);
        Directory.CreateDirectory(_projectDirectory);
        File.WriteAllText(_projectPath, "fixture");
    }

    [Fact]
    public void ValidateWorkspaceRoot_AcceptsANonexistentChildOfAnExistingOrdinaryParent()
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
    [InlineData("workspace_root_is_repository_root")]
    [InlineData("workspace_root_is_project_directory")]
    public void ValidateWorkspaceRoot_RejectsProtectedRootsCaseInsensitively(string expectedCategory)
    {
        var candidate = expectedCategory switch
        {
            "workspace_root_is_drive_root" => Path.GetPathRoot(_testRoot)!,
            "workspace_root_is_user_profile" => ToggleCase(_testRoot),
            "workspace_root_is_repository_root" => ToggleCase(_repositoryRoot),
            "workspace_root_is_project_directory" => ToggleCase(_projectDirectory),
            _ => throw new InvalidOperationException(),
        };

        var result = VciMutationPathPolicy.ValidateWorkspaceRoot(
            candidate,
            _repositoryRoot,
            new[] { _projectPath },
            _testRoot);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCategory, result.RejectionCategory);
    }

    [Fact]
    public void ValidateWorkspaceRoot_RejectsAnExistingRootWithoutDeletingIt()
    {
        var candidate = Path.Combine(_testRoot, "existing-root");
        Directory.CreateDirectory(candidate);
        var marker = Path.Combine(candidate, "keep.txt");
        File.WriteAllText(marker, "keep");

        var result = ValidateRoot(candidate);

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_already_exists", result.RejectionCategory);
        Assert.Equal("keep", File.ReadAllText(marker));
    }

    [Fact]
    public void ValidateWorkspaceRoot_RejectsMissingParent()
    {
        var candidate = Path.Combine(_testRoot, "missing-parent", "workspace-run");

        var result = ValidateRoot(candidate);

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_parent_missing", result.RejectionCategory);
    }

    [Fact]
    public void ValidateWorkspaceRoot_RejectsRawParentTraversalEvenWhenCanonicalPathWouldBeContained()
    {
        var candidate = Path.Combine(_testRoot, "unused", "..", "workspace-run");

        var result = ValidateRoot(candidate);

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_traversal", result.RejectionCategory);
    }

    [Theory]
    [InlineData("\\\\server\\share\\run")]
    [InlineData("\\\\?\\C:\\run")]
    [InlineData("C:\\run:stream")]
    public void ValidateWorkspaceRoot_RejectsUnsupportedWindowsPathSyntax(string candidate)
    {
        var result = ValidateRoot(candidate);

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_unsupported_path_syntax", result.RejectionCategory);
    }

    [Fact]
    public void ValidateWorkspaceRoot_RejectsAnExistingReparseAncestor()
    {
        var parent = Path.Combine(_testRoot, "reparse-parent");
        Directory.CreateDirectory(parent);
        var candidate = Path.Combine(parent, "workspace-run");

        var result = VciMutationPathPolicy.ValidateWorkspaceRoot(
            candidate,
            _repositoryRoot,
            new[] { _projectPath },
            _testRoot,
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase));

        Assert.False(result.IsValid);
        Assert.Equal("workspace_root_reparse_ancestor", result.RejectionCategory);
    }

    [Fact]
    public void ResolveRelativeDirectory_AcceptsAContainedExistingDirectoryWithCaseOnlyRootDifference()
    {
        var workspace = CreateWorkspace();
        var nested = Path.Combine(workspace, "nested");
        Directory.CreateDirectory(nested);

        var result = VciMutationPathPolicy.ResolveRelativeDirectory(ToggleCase(workspace), "nested");

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
        var result = VciMutationPathPolicy.ResolveRelativeDirectory(CreateWorkspace(), relativePath);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCategory, result.RejectionCategory);
    }

    [Fact]
    public void ResolveRelativeDirectory_RejectsAFileValuedDirectoryAndMissingDirectory()
    {
        var workspace = CreateWorkspace();
        File.WriteAllText(Path.Combine(workspace, "file"), "content");

        var fileResult = VciMutationPathPolicy.ResolveRelativeDirectory(workspace, "file");
        var missingResult = VciMutationPathPolicy.ResolveRelativeDirectory(workspace, "missing");

        Assert.Equal("relative_directory_is_file", fileResult.RejectionCategory);
        Assert.Equal("relative_directory_missing", missingResult.RejectionCategory);
    }

    [Fact]
    public void ResolveRelativeDirectory_RejectsAReparseEscapeBeforeResolvingChildren()
    {
        var workspace = CreateWorkspace();
        var reparse = Path.Combine(workspace, "link");
        Directory.CreateDirectory(reparse);

        var result = VciMutationPathPolicy.ResolveRelativeDirectory(
            workspace,
            "link",
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(reparse), StringComparison.OrdinalIgnoreCase));

        Assert.False(result.IsValid);
        Assert.Equal("relative_path_reparse_point", result.RejectionCategory);
    }

    [Theory]
    [InlineData("", "file_name_required")]
    [InlineData("..", "file_name_invalid")]
    [InlineData("sub\\file.xml", "file_name_must_be_leaf")]
    [InlineData("C:\\file.xml", "file_name_must_be_leaf")]
    [InlineData("file.xml:stream", "file_name_alternate_data_stream")]
    [InlineData("bad<name>.xml", "file_name_invalid")]
    public void ResolveFile_RejectsInvalidFileNames(string fileName, string expectedCategory)
    {
        var result = VciMutationPathPolicy.ResolveFile(CreateWorkspace(), string.Empty, fileName);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCategory, result.RejectionCategory);
    }

    [Fact]
    public void ResolveFile_RequiresAnExistingDirectoryAndRejectsDirectoryValuedTargets()
    {
        var workspace = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace, "directory-target.xml"));

        var missingParent = VciMutationPathPolicy.ResolveFile(workspace, "missing", "file.xml");
        var directoryTarget = VciMutationPathPolicy.ResolveFile(workspace, string.Empty, "directory-target.xml");

        Assert.Equal("relative_directory_missing", missingParent.RejectionCategory);
        Assert.Equal("file_target_is_directory", directoryTarget.RejectionCategory);
    }

    [Fact]
    public void ResolveFile_ReturnsAContainedCanonicalPathWithoutCreatingIt()
    {
        var workspace = CreateWorkspace();
        var directory = Path.Combine(workspace, "exports");
        Directory.CreateDirectory(directory);

        var result = VciMutationPathPolicy.ResolveFile(workspace, "exports", "Simulation_DB.xml");

        Assert.True(result.IsValid);
        Assert.Equal(Path.Combine(directory, "Simulation_DB.xml"), result.CanonicalPath);
        Assert.False(File.Exists(result.CanonicalPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private VciMutationPathValidationResult ValidateRoot(string candidate)
        => VciMutationPathPolicy.ValidateWorkspaceRoot(
            candidate,
            _repositoryRoot,
            new[] { _projectPath },
            _testRoot);

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
