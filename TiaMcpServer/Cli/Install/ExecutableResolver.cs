using System.Diagnostics;

namespace TiaMcpServer.Cli.Install;

internal static class ExecutableResolver
{
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

    public static string? FindClientExecutable(string clientExe)
    {
        return RunWhere(clientExe);
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
}
