using System.Diagnostics;
using System.Reflection;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// Kills a client's current worker process so its next request starts a fresh one. A disposed
/// client refuses further requests, so tests that need a restarted worker use this instead.
/// </summary>
internal static class FakeWorkerRestart
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void KillWorkerProcess(OpennessWorkerClient client)
    {
        var transport = typeof(OpennessWorkerClient).GetField("_transport", Private)!.GetValue(client);
        var process = Assert.IsType<Process>(
            typeof(PersistentWorkerTransport).GetField("_process", Private)!.GetValue(transport));
        process.Kill();
        process.WaitForExit();
    }
}
