namespace TiaMcpServer.Tests.TestSupport;

/// <summary>Filesystem targets used only by the scripted lifecycle producer.</summary>
internal sealed class LifecycleProtocolFixture : IDisposable
{
    public LifecycleProtocolFixture(string scenario = "")
    {
        Root = Path.Combine(Path.GetTempPath(), "guarded-lifecycle" + scenario + "-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(Root, "Source");
        var destination = Path.Combine(Root, "Destination");
        ArchiveDirectory = Path.Combine(Root, "Archives");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(ArchiveDirectory);
        SourcePath = Path.Combine(source, "Fixture A.ap21");
        DestinationPath = Path.Combine(destination, "Fixture B.ap21");
        File.WriteAllText(SourcePath, "scripted fixture");
        File.WriteAllText(DestinationPath, "scripted fixture");
    }

    public string Root { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public string ArchiveDirectory { get; }

    public Dictionary<string, object?> Arguments(string tool, bool reject = false) => tool switch
    {
        "open_project" => new() { ["projectPath"] = reject ? " " : DestinationPath },
        "create_project" => new() { ["projectDirectory"] = Root, ["projectName"] = reject ? " " : "Created" },
        "save_project" => new(),
        "save_project_as" => new() { ["targetDirectory"] = Root, ["targetName"] = "Copy", ["rebind"] = !reject },
        "archive_project" => new() { ["archiveDirectory"] = reject ? " " : ArchiveDirectory, ["archiveName"] = "Fixture" },
        "close_project" => new(),
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
