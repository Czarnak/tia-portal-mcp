using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

internal static class StandaloneToolResultFormatter
{
    public static string Format(WorkerCallResult result, string narrowingHint)
    {
        if (result.Success && result.Payload.Length > StructuredOperationBatchPayloadBudget.MaxItemChars)
        {
            var trailerPrefix = $"\n[TRUNCATED — payload exceeded "
                + $"{StructuredOperationBatchPayloadBudget.MaxItemChars} characters. ";
            const string trailerSuffix = "]";
            var maxHintLength = Math.Max(
                0,
                StructuredOperationBatchPayloadBudget.MaxItemChars - trailerPrefix.Length - trailerSuffix.Length);
            var boundedHint = narrowingHint.Substring(
                0,
                Math.Min(narrowingHint.Length, maxHintLength));
            var trailer = trailerPrefix + boundedHint + trailerSuffix;
            var retainedLength = Math.Max(
                0,
                StructuredOperationBatchPayloadBudget.MaxItemChars - trailer.Length);

            result = result with
            {
                Payload = result.Payload.Substring(0, retainedLength) + trailer
            };
        }

        return result.ToEnvelopeText();
    }
}
