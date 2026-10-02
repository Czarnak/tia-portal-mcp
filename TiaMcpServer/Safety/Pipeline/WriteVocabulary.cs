namespace TiaMcpServer.Safety.Pipeline;

/// <summary>The four phases a guarded write response can report.</summary>
public static class WritePhases
{
    /// <summary>A dry run: everything was planned and evaluated, nothing was mutated.</summary>
    public const string Preview = "preview";

    /// <summary>The mutation ran (fully or up to the first failed item) and was verified.</summary>
    public const string Applied = "applied";

    /// <summary>A guard stopped the call before, or between, mutations.</summary>
    public const string Blocked = "blocked";

    /// <summary>The call failed validation, binding, planning, or execution.</summary>
    public const string Error = "error";
}

/// <summary>How a fired guard must be handled.</summary>
public static class WriteGuardSeverities
{
    /// <summary>Reported as a warning; never stops the call.</summary>
    public const string Info = "info";

    /// <summary>Requires user confirmation or satisfaction by the access-mode policy.</summary>
    public const string Acknowledge = "acknowledge";

    /// <summary>Always stops the call.</summary>
    public const string Block = "block";

    /// <summary>True when <paramref name="value"/> is exactly one of the severity constants.</summary>
    public static bool IsKnown(string? value)
        => value is Info or Acknowledge or Block;
}

/// <summary>Who may satisfy an acknowledge guard.</summary>
public static class GuardSatisfactions
{
    /// <summary>The access-mode policy satisfies the guard.</summary>
    public const string Policy = "policy";

    /// <summary>Only the human user may satisfy the guard.</summary>
    public const string User = "user";
}
