using Xunit;

namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// Runs its classes one at a time, after the parallel part of the suite. Two kinds of class
/// belong here:
/// <list type="bullet">
/// <item>Classes that change the process environment, directly or through a helper such as
/// <see cref="FakeWorkerUiOpenProject"/>. FakeWorker reads its scenario from environment
/// variables, and every worker started meanwhile inherits them, so two such classes running at
/// once would leak settings into each other's workers. <see cref="ProcessEnvironmentIsolationTests"/>
/// fails when one of them is not in a serial collection.</item>
/// <item>Classes that spawn a scripted powershell.exe worker behind a fixed 1-5 s request timeout,
/// which can time out when several start at once on a runner with few cores.</item>
/// </list>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealWorkerProcessCollection
{
    public const string Name = "Real worker process";
}
