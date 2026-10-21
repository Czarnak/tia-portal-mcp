namespace TiaMcpServer.Cli;

/// <summary>
/// The one rule for a configured project path: the startup <c>--project</c> option or
/// <see cref="EnvVar"/>, and the path <c>bind_project</c> asserts.
/// </summary>
internal static class ConfiguredProjectPath
{
    public const string EnvVar = "TIA_MCP_PROJECT_PATH";

    /// <summary>Returns null when valid, otherwise an error to prefix with the value's source.</summary>
    public static string? Validate(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && (string.Equals(Path.GetExtension(path), ".ap21", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".amc21", StringComparison.OrdinalIgnoreCase))
            ? null
            : "must be an absolute .ap21 or .amc21 engineering path.";
}
