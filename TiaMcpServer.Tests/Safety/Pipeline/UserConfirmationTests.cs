using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public class UserConfirmationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static ElicitResult Result(string action, string? confirmJson = null) => new()
    {
        Action = action,
        Content = confirmJson is null
            ? null
            : new Dictionary<string, JsonElement> { ["confirm"] = JsonDocument.Parse(confirmJson).RootElement.Clone() },
    };

    private static UserConfirmation Build(ElicitResult result, List<ElicitRequestParams>? sent = null, bool supported = true) =>
        new(supported, (request, _) => { sent?.Add(request); return new ValueTask<ElicitResult>(result); }, Timeout);

    [Fact]
    public async Task AskAsync_returns_unsupported_and_sends_nothing_without_elicitation_capability()
    {
        var sent = new List<ElicitRequestParams>();
        var outcome = await Build(Result("accept", "true"), sent, supported: false).AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.Unsupported, outcome.Outcome);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task AskAsync_confirms_only_on_accept_with_confirm_true()
    {
        var outcome = await Build(Result("accept", "true")).AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.Confirmed, outcome.Outcome);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    public async Task AskAsync_declines_accept_with_non_true_confirm(string json)
    {
        var outcome = await Build(Result("accept", json)).AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.Declined, outcome.Outcome);
    }

    [Fact]
    public async Task AskAsync_declines_accept_with_empty_content()
    {
        var result = new ElicitResult { Action = "accept", Content = new Dictionary<string, JsonElement>() };

        var outcome = await Build(result).AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.Declined, outcome.Outcome);
    }

    [Fact]
    public async Task AskAsync_declines_accept_with_missing_content()
    {
        var outcome = await Build(Result("accept")).AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.Declined, outcome.Outcome);
    }

    [Fact]
    public async Task AskAsync_declines_explicit_decline()
    {
        var outcome = await Build(Result("decline", "true")).AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.Declined, outcome.Outcome);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("something-else")]
    public async Task AskAsync_reports_cancelled_for_cancel_and_unknown_actions(string action)
    {
        var outcome = await Build(Result(action, "true")).AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.Cancelled, outcome.Outcome);
    }

    [Fact]
    public async Task AskAsync_reports_timed_out_when_the_client_never_answers()
    {
        var confirmation = new UserConfirmation(
            true,
            async (_, ct) => { await Task.Delay(System.Threading.Timeout.Infinite, ct); return new ElicitResult(); },
            TimeSpan.FromMilliseconds(50));

        var outcome = await confirmation.AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.TimedOut, outcome.Outcome);
    }

    [Fact]
    public async Task AskAsync_reports_failed_on_transport_exception()
    {
        var confirmation = new UserConfirmation(
            true, (_, _) => throw new InvalidOperationException("boom"), Timeout);

        var outcome = await confirmation.AskAsync("m", CancellationToken.None);

        Assert.Equal(UserConfirmationOutcomes.Failed, outcome.Outcome);
        Assert.Contains("boom", outcome.Detail);
    }

    [Fact]
    public async Task AskAsync_propagates_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var confirmation = new UserConfirmation(
            true,
            async (_, ct) => { await Task.Delay(System.Threading.Timeout.Infinite, ct); return new ElicitResult(); },
            Timeout);

        var pending = confirmation.AskAsync("m", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task AskAsync_sends_one_required_boolean_confirm_field_defaulting_to_false()
    {
        var sent = new List<ElicitRequestParams>();

        await Build(Result("accept", "true"), sent).AskAsync("Overwrite block?", CancellationToken.None);

        var request = Assert.Single(sent);
        Assert.Equal("Overwrite block?", request.Message);
        var schema = Assert.IsType<ElicitRequestParams.RequestSchema>(request.RequestedSchema);
        var property = Assert.Single(schema.Properties!);
        Assert.Equal("confirm", property.Key);
        var boolean = Assert.IsType<ElicitRequestParams.BooleanSchema>(property.Value);
        Assert.False(boolean.Default);
        Assert.Equal(new[] { "confirm" }, schema.Required);
    }
}
