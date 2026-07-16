using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class ProjectSessionBindingTests
{
    [Fact]
    public void FirstExplicitProjectPathBindsSession()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.TryResolve("C:\\Projects\\Line.ap21", out var effectivePath, out var error));

        Assert.Equal("C:\\Projects\\Line.ap21", effectivePath);
        Assert.Null(error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void RepeatedSameProjectPathIsAccepted()
    {
        var binding = new ProjectSessionBinding(null);
        binding.TryResolve("C:\\Projects\\Line.ap21", out _, out _);

        Assert.True(binding.TryResolve("C:\\Projects\\Line.ap21", out var effectivePath, out var error));

        Assert.Equal("C:\\Projects\\Line.ap21", effectivePath);
        Assert.Null(error);
    }

    [Fact]
    public void DifferentProjectPathIsRejectedAfterBinding()
    {
        var binding = new ProjectSessionBinding(null);
        binding.TryResolve("C:\\Projects\\Line.ap21", out _, out _);

        Assert.False(binding.TryResolve("C:\\Projects\\Other.ap21", out var effectivePath, out var error));

        Assert.Null(effectivePath);
        Assert.Contains("already bound", error);
    }

    [Fact]
    public void OmittedProjectPathUsesStartupProjectPath()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Startup.ap21");

        Assert.True(binding.TryResolve(null, out var effectivePath, out var error));

        Assert.Equal("C:\\Projects\\Startup.ap21", effectivePath);
        Assert.Null(error);
    }

    [Fact]
    public void BindSetsUnboundProjectPath()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.Bind("C:\\Projects\\Line.ap21", forceRebind: false, out var error));

        Assert.Null(error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void BindRejectsDifferentProjectPathWithoutForce()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.False(binding.Bind("C:\\Projects\\Other.ap21", forceRebind: false, out var error));

        Assert.Contains("already bound", error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void BindForceRebindsDifferentProjectPath()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.True(binding.Bind("C:\\Projects\\Other.ap21", forceRebind: true, out var error));

        Assert.Null(error);
        Assert.Equal("C:\\Projects\\Other.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void ClearRemovesMatchingProjectBinding()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.True(binding.Clear("C:\\Projects\\Line.ap21", out var error));

        Assert.Null(error);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public void ClearRejectsDifferentProjectPath()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.False(binding.Clear("C:\\Projects\\Other.ap21", out var error));

        Assert.Contains("already bound", error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void TryResolve_RejectionMentionsForceRebindEscapeHatch()
    {
        var binding = new ProjectSessionBinding(@"C:\Projects\a.ap21");

        var resolved = binding.TryResolve(@"C:\Projects\b.ap21", out _, out var error);

        Assert.False(resolved);
        Assert.Contains("forceRebind", error);
        Assert.Contains("open_project", error);
    }
}
