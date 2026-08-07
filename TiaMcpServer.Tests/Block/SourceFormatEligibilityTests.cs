using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class SourceFormatEligibilityTests
{
    [Fact]
    public void A_global_data_block_is_allowed_with_the_db_extension()
    {
        var decision = SourceFormatEligibility.Decide("GlobalDB", "DB", "PLC_1/Blocks/Settings_DB");

        Assert.True(decision.IsAllowed);
        Assert.Equal(".db", decision.Extension);
        Assert.Equal(SourceObjectKind.DataBlock, decision.ExpectedKind);
        Assert.Null(decision.RefusalMessage);
    }

    [Fact]
    public void An_SCL_FB_is_allowed_with_the_scl_extension()
    {
        var decision = SourceFormatEligibility.Decide("FB", "SCL", "PLC_1/Blocks/Thing");

        Assert.True(decision.IsAllowed);
        Assert.Equal(".scl", decision.Extension);
        Assert.Equal(SourceObjectKind.FunctionBlock, decision.ExpectedKind);
    }

    [Fact]
    public void An_SCL_FC_is_allowed_with_the_scl_extension()
    {
        var decision = SourceFormatEligibility.Decide("FC", "SCL", "PLC_1/Blocks/Thing");

        Assert.True(decision.IsAllowed);
        Assert.Equal(".scl", decision.Extension);
        Assert.Equal(SourceObjectKind.Function, decision.ExpectedKind);
    }

    [Fact]
    public void An_SCL_OB_is_allowed_with_the_scl_extension()
    {
        var decision = SourceFormatEligibility.Decide("OB", "SCL", "PLC_1/Blocks/Thing");

        Assert.True(decision.IsAllowed);
        Assert.Equal(".scl", decision.Extension);
        Assert.Equal(SourceObjectKind.OrganizationBlock, decision.ExpectedKind);
    }

    [Fact]
    public void The_language_name_is_matched_case_insensitively()
    {
        var decision = SourceFormatEligibility.Decide("FB", "scl", "PLC_1/Blocks/Thing");

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void A_LAD_function_block_is_refused_and_the_message_names_the_language()
    {
        var decision = SourceFormatEligibility.Decide("FB", "LAD", "PLC_1/Blocks/Inputs_FB");

        Assert.False(decision.IsAllowed);
        Assert.Equal(string.Empty, decision.Extension);
        Assert.NotNull(decision.RefusalMessage);
        Assert.Contains("PLC_1/Blocks/Inputs_FB", decision.RefusalMessage);
        Assert.Contains("LAD", decision.RefusalMessage);
        Assert.Contains("format=xml", decision.RefusalMessage);
    }

    [Fact]
    public void A_GRAPH_function_block_is_refused()
    {
        var decision = SourceFormatEligibility.Decide("FB", "GRAPH", "PLC_1/Blocks/StateMachine");

        Assert.False(decision.IsAllowed);
        Assert.Contains("GRAPH", decision.RefusalMessage);
    }

    [Fact]
    public void An_STL_function_block_is_refused_because_STL_is_out_of_scope()
    {
        var decision = SourceFormatEligibility.Decide("FC", "STL", "PLC_1/Blocks/Legacy");

        Assert.False(decision.IsAllowed);
        Assert.Contains("STL", decision.RefusalMessage);
    }

    [Fact]
    public void An_instance_data_block_is_refused_by_name()
    {
        var decision = SourceFormatEligibility.Decide("InstanceDB", "DB", "PLC_1/Blocks/Damper_DB");

        Assert.False(decision.IsAllowed);
        Assert.Contains("instance data block", decision.RefusalMessage);
    }

    [Fact]
    public void An_array_data_block_is_refused_by_name()
    {
        var decision = SourceFormatEligibility.Decide("ArrayDB", "DB", "PLC_1/Blocks/Buffer_DB");

        Assert.False(decision.IsAllowed);
        Assert.Contains("array data block", decision.RefusalMessage);
    }

    [Fact]
    public void An_unrecognized_kind_is_refused_without_throwing()
    {
        var decision = SourceFormatEligibility.Decide("SomethingElse", "Undef", "PLC_1/Blocks/Odd");

        Assert.False(decision.IsAllowed);
        Assert.Contains("SomethingElse", decision.RefusalMessage);
    }

    [Fact]
    public void The_refusal_message_states_what_source_format_is_available_for()
    {
        var decision = SourceFormatEligibility.Decide("FB", "LAD", "PLC_1/Blocks/Inputs_FB");

        Assert.Contains("global data blocks", decision.RefusalMessage);
        Assert.Contains("SCL", decision.RefusalMessage);
    }
}