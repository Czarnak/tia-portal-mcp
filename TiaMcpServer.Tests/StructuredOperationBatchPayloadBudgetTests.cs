using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Whole-value budgeting for the structured contract. The budget never cuts a JSON value in half:
/// an oversized result is replaced as a whole by an omission carrying retry guidance, and the
/// response is measured as the exact canonical document the caller will receive.
/// </summary>
public class StructuredOperationBatchPayloadBudgetTests
{
    private sealed record TestResponse(string Tool, StructuredOperationBatch Batch);

    private const string RetryTool = "test_read";
    private const string Guidance = "Split the batch.";

    private static TestResponse Compose(StructuredOperationBatch batch) => new("test_read", batch);

    private static int DocumentChars(StructuredOperationBatch batch)
        => CanonicalJson.Serialize(Compose(batch)).Length;

    /// <summary>A canonical result object whose serialized length is exactly <paramref name="chars"/>.</summary>
    private static JsonElement Result(int chars)
    {
        // {"value":"…"} is 12 characters of framing around an unescaped ASCII payload.
        const int Framing = 12;
        Assert.True(chars >= Framing);
        return CanonicalJson.ToElement(new { value = new string('a', chars - Framing) });
    }

    private static StructuredOperationItem Succeeded(
        string operationId,
        JsonElement result,
        string operation = "read_hardware_config",
        IReadOnlyList<string>? warnings = null)
        => new(
            operationId,
            operation,
            OperationBatchStatus.Succeeded,
            result,
            Failure: null,
            Omission: null,
            SkipReason: null,
            warnings ?? Array.Empty<string>());

    private static StructuredOperationItem Failed(
        string operationId,
        string message,
        string category = WorkerFailureCategories.WorkerOperationFailed,
        IReadOnlyList<string>? warnings = null)
        => new(
            operationId,
            "read_hardware_config",
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message),
            Omission: null,
            SkipReason: null,
            warnings ?? Array.Empty<string>());

    private static StructuredOperationBatch Batch(params StructuredOperationItem[] items)
        => StructuredOperationBatch.FromItems(items);

    private static StructuredOperationBatch Apply(
        StructuredOperationBatch batch,
        int maxItemChars = StructuredOperationBatchPayloadBudget.MaxItemChars,
        int maxDocumentChars = StructuredOperationBatchPayloadBudget.MaxDocumentChars)
        => StructuredOperationBatchPayloadBudget.Apply(
            batch,
            Compose,
            RetryTool,
            _ => Guidance,
            maxItemChars,
            maxDocumentChars);

    [Fact]
    public void Apply_LeavesABatchThatAlreadyFitsCompletelyUntouched()
    {
        var batch = Batch(Succeeded("a", Result(100)), Failed("b", "boom", warnings: new[] { "note" }));

        var bounded = Apply(batch);

        Assert.Null(bounded.Truncation);
        Assert.Equal(CanonicalJson.Serialize(batch), CanonicalJson.Serialize(bounded));
    }

    [Fact]
    public void Apply_KeepsAResultOfExactlyTheItemCharacterLimit()
    {
        var result = Result(StructuredOperationBatchPayloadBudget.MaxItemChars);

        var bounded = Apply(Batch(Succeeded("a", result)));

        var item = Assert.Single(bounded.Operations);
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.True(JsonElement.DeepEquals(result, item.Result!.Value));
        Assert.Null(bounded.Truncation);
    }

    [Fact]
    public void Apply_OmitsAResultOneCharacterOverTheItemCharacterLimit()
    {
        const int Original = StructuredOperationBatchPayloadBudget.MaxItemChars + 1;

        var bounded = Apply(Batch(Succeeded("a", Result(Original))));

        var item = Assert.Single(bounded.Operations);
        Assert.Equal(OperationBatchStatus.Omitted, item.Status);

        // Whole-value replacement: the result is gone, not shortened.
        Assert.Null(item.Result);
        Assert.Equal(
            new StructuredOperationOmission(
                StructuredOperationBatchPayloadBudget.ItemLimitReason,
                StructuredOperationBatchPayloadBudget.MaxItemChars,
                Original,
                RetryTool,
                Guidance),
            item.Omission);
        Assert.Equal(new StructuredOperationCounts(0, 0, 1, 0), bounded.Counts);
        Assert.Equal(1, bounded.Truncation!.OmittedResultCount);
        Assert.Equal(new[] { "a" }, bounded.Truncation.AffectedOperationIds);
    }

    /// <summary>Builds a batch whose composed document is exactly <paramref name="target"/> characters.</summary>
    private static StructuredOperationBatch BatchWithDocumentChars(int target)
    {
        // Four results keep each one comfortably under the per-item limit, so this exercises the
        // DOCUMENT budget rather than tripping the item budget first.
        const int Count = 4;
        const int Probe = 1_000;

        static StructuredOperationBatch Build(IReadOnlyList<int> resultChars)
            => Batch(resultChars
                .Select((chars, index) => Succeeded(((char)('a' + index)).ToString(), Result(chars)))
                .ToArray());

        var sizes = Enumerable.Repeat(Probe, Count).ToArray();
        var delta = target - DocumentChars(Build(sizes));

        // Every result grows one character per unit and ASCII payloads never escape, so the
        // document length is linear in the total padding.
        for (var index = 0; index < Count; index++)
        {
            sizes[index] += (delta / Count) + (index < delta % Count ? 1 : 0);
        }

        var solved = Build(sizes);
        Assert.Equal(target, DocumentChars(solved));
        return solved;
    }

    [Fact]
    public void Apply_KeepsADocumentOfExactlyTheDocumentCharacterLimit()
    {
        var batch = BatchWithDocumentChars(StructuredOperationBatchPayloadBudget.MaxDocumentChars);

        var bounded = Apply(batch);

        Assert.Null(bounded.Truncation);
        Assert.Equal(4, bounded.Counts.Succeeded);
        Assert.Equal(
            StructuredOperationBatchPayloadBudget.MaxDocumentChars,
            DocumentChars(bounded));
    }

    [Fact]
    public void Apply_OmitsSuccessfulResultsWhenTheDocumentIsOverTheCharacterLimit()
    {
        var batch = BatchWithDocumentChars(StructuredOperationBatchPayloadBudget.MaxDocumentChars + 2);

        var bounded = Apply(batch);

        Assert.True(
            DocumentChars(bounded) <= StructuredOperationBatchPayloadBudget.MaxDocumentChars,
            $"The bounded document was {DocumentChars(bounded)} characters.");
        Assert.Equal(1, bounded.Counts.Omitted);
        Assert.Equal(
            StructuredOperationBatchPayloadBudget.DocumentLimitReason,
            bounded.Operations.Single(item => item.Status == OperationBatchStatus.Omitted)
                .Omission!.Reason);
    }

    [Fact]
    public void Apply_OmitsTheLargestSuccessfulResultsFirstAndStillPublishesItemsInRequestOrder()
    {
        var batch = Batch(
            Succeeded("small", Result(100)),
            Succeeded("largest", Result(4_000)),
            Succeeded("medium", Result(1_000)));

        var bounded = Apply(batch, maxItemChars: 60_000, maxDocumentChars: 1_600);

        // Order out is order in; only the omission ORDER is size-driven.
        Assert.Equal(
            new[] { "small", "largest", "medium" },
            bounded.Operations.Select(item => item.OperationId).ToArray());
        Assert.Equal(OperationBatchStatus.Succeeded, bounded.Operations[0].Status);
        Assert.Equal(OperationBatchStatus.Omitted, bounded.Operations[1].Status);
        Assert.Equal(OperationBatchStatus.Omitted, bounded.Operations[2].Status);

        // Largest first, so the affected list records "largest" before "medium".
        Assert.Equal(new[] { "largest", "medium" }, bounded.Truncation!.AffectedOperationIds);
    }

    [Fact]
    public void Apply_IsDeterministicAcrossRepeatedRuns()
    {
        var batch = Batch(
            Succeeded("a", Result(1_000)),
            Succeeded("b", Result(1_000)),
            Succeeded("c", Result(1_000)));

        var first = Apply(batch, maxItemChars: 60_000, maxDocumentChars: 1_600);
        var second = Apply(batch, maxItemChars: 60_000, maxDocumentChars: 1_600);

        // Equal-sized candidates must tie-break on request order, not on hash or enumeration luck.
        Assert.Equal(CanonicalJson.Serialize(first), CanonicalJson.Serialize(second));
        Assert.NotEmpty(first.Truncation!.AffectedOperationIds);
        Assert.Equal(
            first.Operations
                .Where(item => item.Status == OperationBatchStatus.Omitted)
                .Select(item => item.OperationId)
                .ToArray(),
            first.Truncation.AffectedOperationIds);
    }

    [Fact]
    public void Apply_KeepsEveryFailureCategoryWhileOmittingSuccessfulResults()
    {
        var batch = Batch(
            Succeeded("big", Result(3_000)),
            Failed("timeout", "the worker did not respond", WorkerFailureCategories.WorkerTimeout),
            Succeeded("also-big", Result(3_000)),
            Failed("protocol", "the payload was rejected", WorkerFailureCategories.ProtocolError));

        var bounded = Apply(batch, maxItemChars: 60_000, maxDocumentChars: 1_500);

        Assert.Equal(2, bounded.Counts.Failed);
        Assert.Equal(2, bounded.Counts.Omitted);
        Assert.Equal(
            new[] { WorkerFailureCategories.WorkerTimeout, WorkerFailureCategories.ProtocolError },
            bounded.Operations.Where(item => item.Failure is not null)
                .Select(item => item.Failure!.Category)
                .ToArray());
    }

    [Fact]
    public void Apply_RemovesCompleteWarningEntriesBeforeShorteningAnyFailureMessage()
    {
        var message = new string('m', 400);
        var batch = Batch(
            Failed("a", message, warnings: new[] { new string('w', 300), new string('x', 300) }),
            Failed("b", message));

        // A budget that the same batch fits once — and only once — its warnings are gone, with
        // room for the truncation record. Shortening a message here would be needless damage.
        var budget = DocumentChars(Batch(Failed("a", message), Failed("b", message))) + 200;

        var bounded = Apply(batch, maxItemChars: 60_000, maxDocumentChars: budget);

        Assert.All(bounded.Operations, item => Assert.Empty(item.Warnings));

        // Warnings alone bought enough room, so the failure evidence itself is untouched.
        Assert.All(bounded.Operations, item => Assert.Equal(message, item.Failure!.Message));
        Assert.Equal(2, bounded.Truncation!.OmittedWarningCount);
        Assert.Equal(0, bounded.Truncation.OmittedResultCount);
    }

    [Fact]
    public void Apply_ShortensFailureMessagesOnlyAsALastResortAndRecordsTheEvidence()
    {
        var message = new string('m', 5_000);
        var batch = Batch(Failed("a", message, warnings: new[] { "note" }), Failed("b", message));
        var originalChars = DocumentChars(batch);

        var bounded = Apply(batch, maxItemChars: 60_000, maxDocumentChars: 900);

        Assert.True(
            DocumentChars(bounded) <= 900,
            $"The bounded document was {DocumentChars(bounded)} characters.");
        Assert.All(
            bounded.Operations,
            item => Assert.True(item.Failure!.Message.Length < message.Length));

        // Categories survive shortening: the reason a call failed is never the part that is cut.
        Assert.All(
            bounded.Operations,
            item => Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, item.Failure!.Category));

        var truncation = bounded.Truncation!;
        Assert.True(truncation.Truncated);
        Assert.Equal(originalChars, truncation.OriginalChars);
        Assert.Equal(DocumentChars(bounded), truncation.PresentedChars);
        Assert.Equal(1, truncation.OmittedWarningCount);
        Assert.Equal(new[] { "a", "b" }, truncation.AffectedOperationIds);
    }

    [Fact]
    public void Apply_ThrowsWhenTheBudgetCannotEvenHoldEveryFailureStatus()
        => Assert.Throws<InvalidOperationException>(
            () => Apply(
                Batch(Failed("a", "boom"), Failed("b", "boom")),
                maxItemChars: 60_000,
                maxDocumentChars: 50));

    [Fact]
    public void Apply_NeverEmitsAPartialJsonValueAndKeepsTextAndStructuredContentIdentical()
    {
        var kept = Result(200);
        var batch = Batch(
            Succeeded("kept", kept),
            Succeeded("dropped", Result(9_000)),
            Failed("failed", "boom"));

        var bounded = Apply(batch, maxItemChars: 60_000, maxDocumentChars: 1_200);
        var result = StructuredToolResult.Create(Compose(bounded), isError: false);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var parsed = JsonDocument.Parse(text);
        Assert.True(
            JsonElement.DeepEquals(parsed.RootElement, Assert.IsType<JsonElement>(result.StructuredContent)));

        // A retained result is byte-identical to the original: budgeting drops whole values and
        // never leaves a half-written object behind.
        Assert.True(JsonElement.DeepEquals(kept, bounded.Operations[0].Result!.Value));
        Assert.Null(bounded.Operations[1].Result);
        Assert.NotNull(bounded.Operations[1].Omission);
    }

    [Fact]
    public void NetworkReadBudget_OmissionInstructsRetryThroughNetworkRead()
    {
        var batch = StructuredOperationBatch.FromItems(new[]
        {
            Succeeded("hardware", Result(5_000), "read_hardware_config"),
            Succeeded("catalog", Result(5_000), "search_equipment_catalog"),
        });

        var bounded = NetworkReadTools.ApplyBudget(batch, maxItemChars: 60_000, maxDocumentChars: 1_400);

        Assert.All(
            bounded.Operations,
            item =>
            {
                Assert.Equal(OperationBatchStatus.Omitted, item.Status);
                Assert.Equal("network_read", item.Omission!.RetryTool);
            });

        var hardware = bounded.Operations[0].Omission!.Guidance;
        var catalog = bounded.Operations[1].Omission!.Guidance;
        Assert.Contains("list_network_objects", hardware, StringComparison.Ordinal);
        Assert.Contains("objectKinds", hardware, StringComparison.Ordinal);
        Assert.Contains("deviceName", hardware, StringComparison.Ordinal);
        Assert.Contains("network_read", hardware, StringComparison.Ordinal);
        Assert.Contains("query", catalog, StringComparison.Ordinal);
        Assert.Contains("maxResults", catalog, StringComparison.Ordinal);
        Assert.Contains("split the batch", catalog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NetworkReadBudget_OversizedInspectionDropsTheWholeValueAndSuggestsFewerAttributes()
    {
        const int OriginalChars = StructuredOperationBatchPayloadBudget.MaxItemChars + 1;
        var batch = Batch(Succeeded(
            "inspection",
            Result(OriginalChars),
            "inspect_network_object"));

        var bounded = NetworkReadTools.ApplyBudget(batch);

        var item = Assert.Single(bounded.Operations);
        Assert.Equal(OperationBatchStatus.Omitted, item.Status);
        Assert.Null(item.Result);
        Assert.Equal(OriginalChars, item.Omission!.OriginalChars);
        Assert.Contains("fewer attributeNames", item.Omission.Guidance, StringComparison.Ordinal);

        var toolResult = StructuredToolResult.Create(
            new NetworkReadResponse("network_read", bounded.IsFullySuccessful, bounded, Error: null),
            isError: false);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(toolResult.Content)).Text;
        using var parsed = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(
            parsed.RootElement,
            Assert.IsType<JsonElement>(toolResult.StructuredContent)));
    }

    [Fact]
    public void NetworkReadBudget_AggregateLimitRemainsOneHundredEightyThousandCharacters()
    {
        var batch = NetworkReadBatchWithDocumentChars(
            StructuredOperationBatchPayloadBudget.MaxDocumentChars + 1);

        var bounded = NetworkReadTools.ApplyBudget(batch);
        var presented = CanonicalJson.Serialize(
            new NetworkReadResponse("network_read", bounded.IsFullySuccessful, bounded, Error: null));

        Assert.Equal(180_000, StructuredOperationBatchPayloadBudget.MaxDocumentChars);
        Assert.True(presented.Length <= 180_000, $"The bounded document was {presented.Length} characters.");
        var omission = Assert.Single(bounded.Operations, item => item.Status == OperationBatchStatus.Omitted).Omission!;
        Assert.Equal(StructuredOperationBatchPayloadBudget.DocumentLimitReason, omission.Reason);
        Assert.Equal("network_read", omission.RetryTool);
        Assert.Contains("list_network_objects", omission.Guidance, StringComparison.Ordinal);
    }

    private static StructuredOperationBatch NetworkReadBatchWithDocumentChars(int target)
    {
        const int Count = 4;
        const int Probe = 1_000;

        static StructuredOperationBatch Build(IReadOnlyList<int> resultChars)
            => Batch(resultChars
                .Select((chars, index) => Succeeded(
                    $"hardware-{index}",
                    Result(chars),
                    "read_hardware_config"))
                .ToArray());

        static int Chars(StructuredOperationBatch value)
            => CanonicalJson.Serialize(
                new NetworkReadResponse("network_read", value.IsFullySuccessful, value, Error: null)).Length;

        var sizes = Enumerable.Repeat(Probe, Count).ToArray();
        var delta = target - Chars(Build(sizes));
        for (var index = 0; index < Count; index++)
        {
            sizes[index] += (delta / Count) + (index < delta % Count ? 1 : 0);
        }

        var solved = Build(sizes);
        Assert.Equal(target, Chars(solved));
        return solved;
    }

    [Fact]
    public void NetworkReadBudget_MeasuresTheExactNetworkReadResponseDocument()
    {
        var batch = StructuredOperationBatch.FromItems(new[] { Succeeded("hardware", Result(5_000)) });

        var bounded = NetworkReadTools.ApplyBudget(batch, maxItemChars: 60_000, maxDocumentChars: 5_100);

        // 5,000 characters of result fit the item budget but not the envelope-inclusive document
        // budget, which is exactly the difference between budgeting a value and budgeting the
        // response the caller receives.
        var item = Assert.Single(bounded.Operations);
        Assert.Equal(OperationBatchStatus.Omitted, item.Status);
        Assert.Equal(
            StructuredOperationBatchPayloadBudget.DocumentLimitReason,
            item.Omission!.Reason);
    }
}
