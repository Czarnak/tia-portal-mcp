namespace TiaMcpServer.Contracts.Block;

public static class ObNumberRules
{
    public const int FirstFree = 123;
    public const int Max = 32767;

    /// <summary>
    /// A singleton gets its base number; a multi-instance class gets its base number if no OB holds it,
    /// otherwise the lowest number in 123-32767 no OB holds. <paramref name="usedObNumbers"/> holds OB
    /// numbers only. Returns null when the singleton's number is held or the range is exhausted.
    /// </summary>
    public static int? Pick(ObEventClass cls, ICollection<int> usedObNumbers)
    {
        if (!usedObNumbers.Contains(cls.BaseNumber))
        {
            return cls.BaseNumber;
        }

        if (cls.IsSingleton)
        {
            return null;
        }

        for (var number = FirstFree; number <= Max; number++)
        {
            if (!usedObNumbers.Contains(number))
            {
                return number;
            }
        }

        return null;
    }
}
