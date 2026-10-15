using System.Globalization;

namespace TiaMcpServer.Cli;

public enum WorkerTimeoutSource
{
    Default,
    Option,
    Environment
}

public sealed record WorkerTimeoutParseResult(bool IsValid, int Minutes, WorkerTimeoutSource Source, string? Error)
{
    public TimeSpan Timeout => TimeSpan.FromMinutes(Minutes);

    public static string SourceName(WorkerTimeoutSource source) => source switch
    {
        WorkerTimeoutSource.Default => "default",
        WorkerTimeoutSource.Option => "option",
        WorkerTimeoutSource.Environment => "environment",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
}

public static class WorkerTimeoutParser
{
    public const string Flag = "--worker-timeout-minutes";
    public const string EnvVar = "TIA_MCP_WORKER_TIMEOUT_MINUTES";
    public const int DefaultMinutes = 5;
    public const int MaxMinutes = 60;

    public static WorkerTimeoutParseResult Parse(string[] args, Func<string, string?>? getEnvironmentVariable = null)
    {
        var option = StartupOptionReader.ReadSingle(args, Flag);
        if (option.Error is not null)
            return new(false, 0, WorkerTimeoutSource.Option, option.Error);

        var text = option.Value;
        var source = WorkerTimeoutSource.Option;
        if (text is null)
        {
            getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
            text = getEnvironmentVariable(EnvVar);
            if (string.IsNullOrWhiteSpace(text))
                return new(true, DefaultMinutes, WorkerTimeoutSource.Default, null);
            source = WorkerTimeoutSource.Environment;
        }

        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            && minutes >= DefaultMinutes && minutes <= MaxMinutes)
        {
            return new(true, minutes, source, null);
        }

        var sourceName = source == WorkerTimeoutSource.Option ? Flag : EnvVar;
        return new(false, 0, source,
            $"Invalid {sourceName} value '{text}'. Expected a whole number of minutes from {DefaultMinutes} to {MaxMinutes}.");
    }
}
