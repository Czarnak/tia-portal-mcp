using System.Diagnostics;

namespace TiaMcpServer.Cli.Install;

internal static class ExecutableResolver
{
    private static readonly string[] WindowsExtensions = { ".exe", ".cmd", ".bat" };

    public static string? ResolveServerExecutable(string? serverPath)
    {
        // 1. Explicit --server-path
        if (!string.IsNullOrWhiteSpace(serverPath))
        {
            if (File.Exists(serverPath))
            {
                return Path.GetFullPath(serverPath);
            }

            return null;
        }

        // 2. where.exe tia-mcp
        var whereResult = RunWhere("tia-mcp");
        if (whereResult is not null)
        {
            return whereResult;
        }

        // 3. %USERPROFILE%\.dotnet\tools\tia-mcp.exe
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            var dotnetToolPath = Path.Combine(userProfile, ".dotnet", "tools", "tia-mcp.exe");
            if (File.Exists(dotnetToolPath))
            {
                return dotnetToolPath;
            }
        }

        // 4. Environment.ProcessPath (if tia-mcp)
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("tia-mcp", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(processPath))
        {
            return processPath;
        }

        return null;
    }

    public static ExecutableResolutionResult ResolveClientExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ExecutableResolutionResult(false, command, null, ExecutableKind.Native,
                "Command is empty.");
        }

        // 1. Absolute path
        if (Path.IsPathRooted(command))
        {
            return ResolveAbsolute(command);
        }

        // 2. where.exe with extensions
        var whereResult = ResolveViaWhere(command);
        if (whereResult is not null)
        {
            return whereResult;
        }

        // 3. Common directories
        var commonResult = ResolveFromCommonDirectories(command);
        if (commonResult is not null)
        {
            return commonResult;
        }

        // 4. Not found
        return new ExecutableResolutionResult(false, command, null, ExecutableKind.Native,
            $"The executable '{command}' was not found.");
    }

    /// <summary>
    /// Legacy method for backward compatibility. Returns only the path or null.
    /// </summary>
    public static string? FindClientExecutable(string clientExe)
    {
        var result = ResolveClientExecutable(clientExe);
        return result.Found ? result.ResolvedPath : null;
    }

    private static ExecutableResolutionResult ResolveAbsolute(string path)
    {
        if (File.Exists(path))
        {
            var kind = ClassifyExtension(path);
            return new ExecutableResolutionResult(true, path, Path.GetFullPath(path), kind, null);
        }

        // Try adding extensions
        foreach (var ext in WindowsExtensions)
        {
            var withExt = path + ext;
            if (File.Exists(withExt))
            {
                return new ExecutableResolutionResult(true, path, Path.GetFullPath(withExt),
                    ClassifyExtension(withExt), null);
            }
        }

        return new ExecutableResolutionResult(false, path, null, ExecutableKind.Native,
            $"The path '{path}' does not exist.");
    }

    private static ExecutableResolutionResult? ResolveViaWhere(string command)
    {
        // Try where.exe with the bare command first, then with each extension
        var candidates = new List<string> { command };
        foreach (var ext in WindowsExtensions)
        {
            candidates.Add(command + ext);
        }

        foreach (var candidate in candidates)
        {
            var lines = RunWhereAll(candidate);
            if (lines.Count == 0) continue;

            // where.exe returns existing paths; pick the preferred executable kind.
            var best = lines
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ExtensionPriority)
                .First();

            return new ExecutableResolutionResult(true, command, best,
                ClassifyExtension(best), null);
        }

        return null;
    }

    private static ExecutableResolutionResult? ResolveFromCommonDirectories(string command)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var searchDirs = new List<string>();

        if (!string.IsNullOrEmpty(userProfile))
        {
            searchDirs.Add(Path.Combine(userProfile, ".local", "bin"));
            searchDirs.Add(Path.Combine(userProfile, ".dotnet", "tools"));
        }

        if (!string.IsNullOrEmpty(appData))
        {
            searchDirs.Add(Path.Combine(appData, "npm"));
        }

        if (!string.IsNullOrEmpty(localAppData))
        {
            searchDirs.Add(Path.Combine(localAppData, "Programs"));
        }

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            // Try with each extension, then bare
            foreach (var ext in WindowsExtensions)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                {
                    return new ExecutableResolutionResult(true, command, candidate,
                        ClassifyExtension(candidate), null);
                }
            }

            // Try bare name
            var bare = Path.Combine(dir, command);
            if (File.Exists(bare))
            {
                return new ExecutableResolutionResult(true, command, bare,
                    ClassifyExtension(bare), null);
            }
        }

        return null;
    }

    internal static ExecutableKind ClassifyExtension(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase)
                ? ExecutableKind.CommandScript
                : ExecutableKind.BatchScript;
        }

        return ExecutableKind.Native;
    }

    private static int ExtensionPriority(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".exe" => 0,
            ".cmd" => 1,
            ".bat" => 2,
            _ => 3
        };
    }

    private static string? RunWhere(string executable)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(executable);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            process.WaitForExit(5000);
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(stdout))
            {
                // where.exe may return multiple lines; take the first
                var firstLine = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                return firstLine;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> RunWhereAll(string executable)
    {
        var results = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(executable);

            using var process = Process.Start(psi)!;
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                return results;
            }

            results.AddRange(process.StandardOutput.ReadToEnd().Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch
        {
            // where.exe not available or failed
        }

        return results;
    }
}
