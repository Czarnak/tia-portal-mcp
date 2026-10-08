using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// The numbers held by OBs in a PLC: the PLC and each software unit, through user and system
/// block groups. Only OBs count; other block kinds have their own number spaces.
/// </summary>
internal static class ObNumberScanner
{
    public static HashSet<int> Collect(PlcSoftware plc)
    {
        var numbers = new HashSet<int>();
        try
        {
            foreach (var owner in BlockTargetResolver.EnumerateOwners(plc))
            {
                CollectGroup(owner.RootBlockGroup, numbers);
            }
        }
        catch (EngineeringException ex)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                $"The OB numbers of PLC '{plc.Name}' could not be read: {ex.Message}");
        }

        return numbers;
    }

    private static void CollectGroup(PlcBlockGroup group, HashSet<int> numbers)
    {
        foreach (PlcBlock block in group.Blocks)
        {
            if (block is OB)
            {
                numbers.Add(block.Number);
            }
        }

        foreach (PlcBlockGroup child in group.Groups)
        {
            CollectGroup(child, numbers);
        }

        if (group is PlcBlockSystemGroup root)
        {
            foreach (PlcSystemBlockGroup child in root.SystemBlockGroups)
            {
                CollectSystemGroup(child, numbers);
            }
        }
    }

    private static void CollectSystemGroup(PlcSystemBlockGroup group, HashSet<int> numbers)
    {
        foreach (PlcBlock block in group.Blocks)
        {
            if (block is OB)
            {
                numbers.Add(block.Number);
            }
        }

        foreach (PlcSystemBlockGroup child in group.Groups)
        {
            CollectSystemGroup(child, numbers);
        }
    }
}
