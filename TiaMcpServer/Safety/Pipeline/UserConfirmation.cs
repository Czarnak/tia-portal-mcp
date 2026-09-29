using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>The ways a human confirmation prompt can end.</summary>
public static class UserConfirmationOutcomes
{
    /// <summary>The user accepted and ticked the confirm box.</summary>
    public const string Confirmed = "confirmed";

    /// <summary>The user declined, or accepted without confirming (an auto-accepting client sends <c>{}</c>).</summary>
    public const string Declined = "declined";

    /// <summary>The user dismissed the prompt, or the client answered with an unknown action.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The client did not answer within the timeout.</summary>
    public const string TimedOut = "timed_out";

    /// <summary>The client does not advertise elicitation; no request was sent.</summary>
    public const string Unsupported = "unsupported";

    /// <summary>The request failed in transit or the client returned an error.</summary>
    public const string Failed = "failed";
}

/// <summary>Outcome of one confirmation prompt; <see cref="Detail"/> carries diagnostic text for <c>failed</c>.</summary>
public sealed record UserConfirmationResult(string Outcome, string? Detail = null)
{
    /// <summary>True only when the user explicitly confirmed.</summary>
    public bool IsConfirmed => Outcome == UserConfirmationOutcomes.Confirmed;
}

/// <summary>
/// Asks the human behind the client a yes/no question through MCP form elicitation. Only an
/// explicit <c>accept</c> with <c>confirm: true</c> counts as confirmation.
/// </summary>
public sealed class UserConfirmation
{
    private const string ConfirmField = "confirm";

    private readonly bool _clientSupportsElicitation;
    private readonly Func<ElicitRequestParams, CancellationToken, ValueTask<ElicitResult>> _elicit;
    private readonly TimeSpan _timeout;

    public UserConfirmation(
        bool clientSupportsElicitation,
        Func<ElicitRequestParams, CancellationToken, ValueTask<ElicitResult>> elicit,
        TimeSpan timeout)
    {
        _clientSupportsElicitation = clientSupportsElicitation;
        _elicit = elicit ?? throw new ArgumentNullException(nameof(elicit));
        _timeout = timeout;
    }

    /// <summary>Binds to a live server, reading the elicitation capability the client advertised.</summary>
    public static UserConfirmation For(McpServer server, TimeSpan timeout) =>
        new(server.ClientCapabilities?.Elicitation is not null,
            (request, ct) => new ValueTask<ElicitResult>(server.ElicitAsync(request, ct).AsTask()),
            timeout);

    public async Task<UserConfirmationResult> AskAsync(string message, CancellationToken cancellationToken)
    {
        if (!_clientSupportsElicitation)
        {
            return new UserConfirmationResult(UserConfirmationOutcomes.Unsupported);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        try
        {
            var result = await _elicit(BuildRequest(message), timeoutSource.Token).ConfigureAwait(false);
            return new UserConfirmationResult(Classify(result));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UserConfirmationResult(UserConfirmationOutcomes.TimedOut);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UserConfirmationResult(UserConfirmationOutcomes.Failed, ex.Message);
        }
    }

    private static ElicitRequestParams BuildRequest(string message) => new()
    {
        Message = message,
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
            {
                [ConfirmField] = new ElicitRequestParams.BooleanSchema { Default = false },
            },
            Required = new List<string> { ConfirmField },
        },
    };

    private static string Classify(ElicitResult result) => result.Action switch
    {
        "accept" => IsConfirmTrue(result) ? UserConfirmationOutcomes.Confirmed : UserConfirmationOutcomes.Declined,
        "decline" => UserConfirmationOutcomes.Declined,
        _ => UserConfirmationOutcomes.Cancelled,
    };

    private static bool IsConfirmTrue(ElicitResult result) =>
        result.Content is not null
        && result.Content.TryGetValue(ConfirmField, out var value)
        && value.ValueKind == JsonValueKind.True;
}
