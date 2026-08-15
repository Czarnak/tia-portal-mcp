using System.Collections.Generic;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class IoChannelControllerResolverTests
{
    private const string ItemDesc = "Module_1";

    [Fact]
    public void Resolve_OneAddressAndOneController_ResolvesSuccessfully()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 4,
                Length = 2,
                ControllerNames = new[] { "PLC_1" },
                ControllerAssociationReadable = true,
            },
        };

        var result = IoChannelControllerResolver.Resolve("Input", 32, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.Resolved, result.Status);
        Assert.Equal("PLC_1", result.ControllerName);
        Assert.Null(result.DiagnosticMessage);
        Assert.True(result.IsTargetMatch("PLC_1"));
        Assert.False(result.IsTargetMatch("PLC_2"));
    }

    [Fact]
    public void Resolve_InputAndOutputAddressesOnSameItem_ResolvesRespectiveChannelsWithoutInterference()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2,
                ControllerNames = new[] { "PLC_IN" },
                ControllerAssociationReadable = true,
            },
            new()
            {
                IoType = "Output",
                StartAddress = 0,
                Length = 2,
                ControllerNames = new[] { "PLC_OUT" },
                ControllerAssociationReadable = true,
            },
        };

        var inResult = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);
        var outResult = IoChannelControllerResolver.Resolve("Output", 0, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.Resolved, inResult.Status);
        Assert.Equal("PLC_IN", inResult.ControllerName);
        Assert.True(inResult.IsTargetMatch("PLC_IN"));

        Assert.Equal(IoChannelControllerStatus.Resolved, outResult.Status);
        Assert.Equal("PLC_OUT", outResult.ControllerName);
        Assert.True(outResult.IsTargetMatch("PLC_OUT"));
    }

    [Fact]
    public void Resolve_DifferentControllersForNonOverlappingRanges_ResolvesRespectiveChannels()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2,
                ControllerNames = new[] { "PLC_A" },
                ControllerAssociationReadable = true,
            },
            new()
            {
                IoType = "Input",
                StartAddress = 4,
                Length = 2,
                ControllerNames = new[] { "PLC_B" },
                ControllerAssociationReadable = true,
            },
        };

        var ch0 = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);
        var ch32 = IoChannelControllerResolver.Resolve("Input", 32, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.Resolved, ch0.Status);
        Assert.Equal("PLC_A", ch0.ControllerName);

        Assert.Equal(IoChannelControllerStatus.Resolved, ch32.Status);
        Assert.Equal("PLC_B", ch32.ControllerName);
    }

    [Fact]
    public void Resolve_MultipleContainingAddresses_FailsClosedAsAmbiguous()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 4,
                ControllerNames = new[] { "PLC_1" },
            },
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2,
                ControllerNames = new[] { "PLC_1" },
            },
        };

        var result = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.MultipleContainingAddresses, result.Status);
        Assert.Null(result.ControllerName);
        Assert.NotNull(result.DiagnosticMessage);
        Assert.Contains("contained by more than one address", result.DiagnosticMessage);
        Assert.False(result.IsTargetMatch("PLC_1"));
    }

    [Fact]
    public void Resolve_BoundaryContainment_ResolvesExactStartAndEndBits()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 10,
                Length = 2, // bytes 10..11 -> bits 80..95 (half-open [80, 96))
                ControllerNames = new[] { "PLC_1" },
            },
        };

        // Channel at exact start bit (80, width 1)
        var startResult = IoChannelControllerResolver.Resolve("Input", 80, 1u, addresses, ItemDesc);
        Assert.Equal(IoChannelControllerStatus.Resolved, startResult.Status);

        // Channel at exact last bit (95, width 1) -> interval [95, 96)
        var endResult = IoChannelControllerResolver.Resolve("Input", 95, 1u, addresses, ItemDesc);
        Assert.Equal(IoChannelControllerStatus.Resolved, endResult.Status);

        // Channel for entire 16-bit word at start (80, width 16) -> interval [80, 96)
        var wordResult = IoChannelControllerResolver.Resolve("Input", 80, 16u, addresses, ItemDesc);
        Assert.Equal(IoChannelControllerStatus.Resolved, wordResult.Status);

        // Channel 1 bit before start (79, width 1)
        var beforeResult = IoChannelControllerResolver.Resolve("Input", 79, 1u, addresses, ItemDesc);
        Assert.Equal(IoChannelControllerStatus.NoContainingAddress, beforeResult.Status);

        // Channel 1 bit past end (96, width 1)
        var pastResult = IoChannelControllerResolver.Resolve("Input", 96, 1u, addresses, ItemDesc);
        Assert.Equal(IoChannelControllerStatus.NoContainingAddress, pastResult.Status);
    }

    [Fact]
    public void Resolve_PartiallyOverlappingChannels_FailsClosedWithNoContainingAddress()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2, // bits [0, 16)
                ControllerNames = new[] { "PLC_1" },
            },
        };

        // Channel starting at bit 8 with width 16 -> interval [8, 24), overhangs past bit 16
        var result = IoChannelControllerResolver.Resolve("Input", 8, 16u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.NoContainingAddress, result.Status);
        Assert.Null(result.ControllerName);
        Assert.NotNull(result.DiagnosticMessage);
        Assert.Contains("No controller association was found", result.DiagnosticMessage);
    }

    [Theory]
    [InlineData(0, 0)] // zero-length
    [InlineData(0, -1)] // negative length
    [InlineData(-1, 2)] // negative start
    [InlineData(null, 2)] // null start
    [InlineData(0, null)] // null length
    public void Resolve_ZeroLengthNullAndNegativeNormalizedRanges_AreIgnored(int? start, int? length)
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = start,
                Length = length,
                ControllerNames = new[] { "PLC_1" },
            },
        };

        var result = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.NoContainingAddress, result.Status);
    }

    [Fact]
    public void Resolve_OverflowingRanges_HandledSafelyWithoutThrowing()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = int.MaxValue / 8,
                Length = int.MaxValue / 8,
                ControllerNames = new[] { "PLC_1" },
            },
        };

        var result = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.NoContainingAddress, result.Status);
    }

    [Fact]
    public void Resolve_UnreadableAssociationOnRelevantAddress_FailsClosedWithDiagnostic()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2,
                ControllerNames = Array.Empty<string>(),
                ControllerAssociationReadable = false,
            },
        };

        var result = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.UnreadableController, result.Status);
        Assert.Null(result.ControllerName);
        Assert.NotNull(result.DiagnosticMessage);
        Assert.Contains("Controller association was unreadable", result.DiagnosticMessage);
        Assert.False(result.IsTargetMatch("PLC_1"));
    }

    [Fact]
    public void Resolve_UnreadableAssociationOnUnrelatedAddress_DoesNotSuppressRelevantAddress()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2,
                ControllerNames = new[] { "PLC_1" },
                ControllerAssociationReadable = true,
            },
            new()
            {
                IoType = "Input",
                StartAddress = 10,
                Length = 2,
                ControllerNames = Array.Empty<string>(),
                ControllerAssociationReadable = false, // unreadable, but for unrelated range 10..11
            },
            new()
            {
                IoType = "Output",
                StartAddress = 0,
                Length = 2,
                ControllerNames = Array.Empty<string>(),
                ControllerAssociationReadable = false, // unreadable, but for different I/O area
            },
        };

        var result = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.Resolved, result.Status);
        Assert.Equal("PLC_1", result.ControllerName);
        Assert.Null(result.DiagnosticMessage);
        Assert.True(result.IsTargetMatch("PLC_1"));
    }

    [Fact]
    public void Resolve_NoCrossControllerMatching_SingleDifferentControllerYieldsNoMatch()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2,
                ControllerNames = new[] { "PLC_OTHER" },
                ControllerAssociationReadable = true,
            },
        };

        var result = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.Resolved, result.Status);
        Assert.Equal("PLC_OTHER", result.ControllerName);
        Assert.Null(result.DiagnosticMessage); // different controller is normal operation, not an error
        Assert.False(result.IsTargetMatch("PLC_SELECTED"));
    }

    [Fact]
    public void Resolve_MultipleControllersOnSingleAddress_FailsClosedAsAmbiguous()
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2,
                ControllerNames = new[] { "PLC_1", "PLC_2" },
                ControllerAssociationReadable = true,
            },
        };

        var result = IoChannelControllerResolver.Resolve("Input", 0, 1u, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.MultipleControllers, result.Status);
        Assert.Null(result.ControllerName);
        Assert.NotNull(result.DiagnosticMessage);
        Assert.Contains("owned by more than one controller", result.DiagnosticMessage);
        Assert.False(result.IsTargetMatch("PLC_1"));
    }

    [Theory]
    [InlineData(null, 0, 1u)]
    [InlineData("Complex", 0, 1u)]
    [InlineData("Input", null, 1u)]
    [InlineData("Input", 0, null)]
    [InlineData("Input", -1, 1u)]
    [InlineData("Input", 0, 0u)]
    public void Resolve_InvalidOrMissingChannelEvidence_ReturnsNoInterval(
        string? ioType,
        int? startBit,
        uint? widthBits)
    {
        var addresses = new List<IoAddressRecord>
        {
            new()
            {
                IoType = "Input",
                StartAddress = 0,
                Length = 2,
                ControllerNames = new[] { "PLC_1" },
            },
        };

        var result = IoChannelControllerResolver.Resolve(ioType, startBit, widthBits, addresses, ItemDesc);

        Assert.Equal(IoChannelControllerStatus.NoInterval, result.Status);
        Assert.Null(result.ControllerName);
        Assert.Null(result.DiagnosticMessage);
    }
}
