using TiaMcpServer.Contracts.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Contracts;

public sealed class PlcNameRulesTests
{
    [Fact]
    public void Comparer_IsOrdinalIgnoreCase()
        => Assert.Same(StringComparer.OrdinalIgnoreCase, PlcNameRules.Comparer);
}
