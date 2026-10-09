namespace TiaMcpServer.Contracts.Block;

/// <summary>One offered organization-block event class: its name, base number and whether only one OB may exist.</summary>
public sealed class ObEventClass
{
    public ObEventClass(string name, int baseNumber, bool isSingleton)
    {
        Name = name;
        BaseNumber = baseNumber;
        IsSingleton = isSingleton;
    }

    public string Name { get; }

    public int BaseNumber { get; }

    public bool IsSingleton { get; }
}

/// <summary>The 15 OB event classes offered by create_block (SynchronousCycle is excluded), in documentation order.</summary>
public static class ObEventClasses
{
    public const string Default = "ProgramCycle";

    public static IReadOnlyList<ObEventClass> All { get; } = new[]
    {
        new ObEventClass("ProgramCycle", 1, false),
        new ObEventClass("Startup", 100, false),
        new ObEventClass("TimeOfDay", 10, false),
        new ObEventClass("TimeDelayInterrupt", 20, false),
        new ObEventClass("CyclicInterrupt", 30, false),
        new ObEventClass("HardwareInterrupt", 40, false),
        new ObEventClass("Status", 55, true),
        new ObEventClass("Update", 56, true),
        new ObEventClass("Profile", 57, true),
        new ObEventClass("TimeErrorInterrupt", 80, true),
        new ObEventClass("DiagnosticErrorInterrupt", 82, true),
        new ObEventClass("PullOrPlugOfModules", 83, true),
        new ObEventClass("RackOrStationFailure", 86, true),
        new ObEventClass("ProgrammingError", 121, true),
        new ObEventClass("IOAccessError", 122, true),
    };

    /// <summary>Ordinal (case-sensitive) lookup.</summary>
    public static bool TryGet(string name, out ObEventClass cls)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                cls = candidate;
                return true;
            }
        }

        cls = null!;
        return false;
    }

    public static string NamesForMessage() => string.Join(", ", All.Select(c => c.Name));
}
