using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Groups the test classes that spawn a real OS process (powershell.exe, scripted to speak the
/// worker IPC protocol) behind a fixed request timeout (1-5s). xUnit runs test collections in
/// parallel by default; on a CI runner with few cores, several of these classes launching
/// PowerShell concurrently can starve each other long enough to blow through their timeout and
/// fail with a transport-level error (WorkerTimeout/WorkerCrashed), even though nothing in the
/// production code or the scripted worker is wrong. Tagging every such class with
/// <c>[Collection(Name)]</c> makes xUnit run them sequentially relative to each other while still
/// running the rest of the (fast, in-process) suite in parallel, removing the contention without
/// serializing the whole test run.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealWorkerProcessCollection
{
    public const string Name = "Real worker process";
}
