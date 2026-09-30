namespace TiaMcpServer.Cli;

/// <summary>Resolves the startup confirmation setting; it is independent of access mode.</summary>
public static class UserConfirmationParser
{
    public static UserConfirmationParseResult Parse(string[] args)
    {
        bool? explicitValue = null;
        foreach (var (arg, index) in args.Select((arg, index) => (arg, index)))
        {
            bool candidate;
            if (string.Equals(arg, "--confirm-with-user", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return UserConfirmationParseResult.Fail(
                        "--confirm-with-user is an on switch. Use --confirm-with-user=false to disable it.");
                }

                candidate = true;
            }
            else if (arg.StartsWith("--confirm-with-user=", StringComparison.OrdinalIgnoreCase))
            {
                if (!bool.TryParse(arg.Substring("--confirm-with-user=".Length), out candidate))
                {
                    return UserConfirmationParseResult.Fail(
                        "--confirm-with-user requires 'true' or 'false' after '='.");
                }
            }
            else
            {
                continue;
            }

            if (explicitValue is not null && explicitValue.Value != candidate)
            {
                return UserConfirmationParseResult.Fail("Conflicting --confirm-with-user arguments were supplied.");
            }

            explicitValue = candidate;
        }

        return new(true, explicitValue ?? true, null);
    }
}

public sealed record UserConfirmationParseResult(bool IsValid, bool ConfirmWithUser, string? Error)
{
    public static UserConfirmationParseResult Fail(string error) => new(false, true, error);
}
