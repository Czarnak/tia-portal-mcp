using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class WorkerResponse
{
    public bool Success { get; set; }

    public string? Payload { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// Non-fatal degradation notes captured from the worker's Console.Error while THIS
    /// request was being handled (e.g. "Skipping device X: access denied"). Null when none.
    /// </summary>
    public List<string>? Warnings { get; set; }
}
