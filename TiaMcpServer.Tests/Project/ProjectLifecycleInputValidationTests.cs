using Siemens.Engineering;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectLifecycleInputValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative.ap21")]
    public void InvalidFilePath_IsValidationError(string? path)
        => ValidationError(() => ProjectLifecycleService.RequireAbsoluteFile(path!, "ProjectPath"));

    [Fact]
    public void MissingFile_IsValidationError()
        => ValidationError(() => ProjectLifecycleService.RequireAbsoluteFile(MissingPath(), "ProjectPath"));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", true)]
    [InlineData(" ", false)]
    [InlineData("relative", true)]
    public void InvalidDirectoryPath_IsValidationError(string? path, bool mustExist)
        => ValidationError(() => ProjectLifecycleService.RequireAbsoluteDirectory(path!, "TargetDirectory", mustExist));

    [Fact]
    public void MissingRequiredDirectory_IsValidationError()
        => ValidationError(() => ProjectLifecycleService.RequireAbsoluteDirectory(MissingPath(), "TargetDirectory", mustExist: true));

    [Fact]
    public void MissingArchiveDirectory_IsValidationError()
        => ValidationError(() => ProjectLifecycleService.RequireDirectoryExists(MissingPath(), "ArchiveDirectory"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingName_IsValidationError(string? name)
        => ValidationError(() => ProjectLifecycleService.RequireName(name, "TargetName"));

    [Theory]
    [InlineData("")]
    [InlineData("unsupported")]
    public void InvalidArchiveMode_IsValidationError(string mode)
        => ValidationError(() => ProjectLifecycleService.ParseArchiveMode(mode));

    [Fact]
    public void ValidPathsAndName_AreAccepted()
    {
        ProjectLifecycleService.RequireAbsoluteFile(typeof(ProjectLifecycleInputValidationTests).Assembly.Location, "ProjectPath");
        ProjectLifecycleService.RequireAbsoluteDirectory(Path.GetTempPath(), "TargetDirectory", mustExist: true);
        ProjectLifecycleService.RequireAbsoluteDirectory(MissingPath(), "ArchiveDirectory", mustExist: false);
        ProjectLifecycleService.RequireDirectoryExists(Path.GetTempPath(), "ArchiveDirectory");
        ProjectLifecycleService.RequireName("Fixture", "TargetName");
    }

    [Theory]
    [InlineData("None", ProjectArchivationMode.None)]
    [InlineData("DiscardRestorableData", ProjectArchivationMode.DiscardRestorableData)]
    [InlineData("compressed", ProjectArchivationMode.Compressed)]
    [InlineData("DiscardRestorableDataAndCompressed", ProjectArchivationMode.DiscardRestorableDataAndCompressed)]
    public void ValidArchiveMode_IsParsed(string mode, ProjectArchivationMode expected)
        => Assert.Equal(expected, ProjectLifecycleService.ParseArchiveMode(mode));

    private static string MissingPath()
        => Path.Combine(Path.GetTempPath(), "tia-missing-" + Guid.NewGuid().ToString("N"));

    private static void ValidationError(Action validate)
    {
        // The generic mapper must let the categorized failure reach Program's dedicated catch.
        var error = Assert.Throws<WorkerOperationException>(() => WorkerExecution.Execute(() =>
        {
            validate();
            return new WorkerResponse { Success = true };
        }, () => 0));

        Assert.Equal(WorkerFailureCategories.ValidationError, error.FailureCategory);
    }
}
