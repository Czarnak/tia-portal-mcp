using TiaMcpServer.Batch;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

internal static class StandaloneToolResultFormatter
{
    public static string Format(WorkerCallResult result, string narrowingHint)
    {
        if (result.Success && result.Payload.Length > BatchPayloadBudget.MaxItemChars)
        {
            var trailerPrefix = $"\n[TRUNCATED — payload exceeded "
                + $"{BatchPayloadBudget.MaxItemChars} characters. ";
            const string trailerSuffix = "]";
            var maxHintLength = Math.Max(
                0,
                BatchPayloadBudget.MaxItemChars - trailerPrefix.Length - trailerSuffix.Length);
            var boundedHint = narrowingHint.Substring(
                0,
                Math.Min(narrowingHint.Length, maxHintLength));
            var trailer = trailerPrefix + boundedHint + trailerSuffix;
            var retainedLength = Math.Max(
                0,
                BatchPayloadBudget.MaxItemChars - trailer.Length);

            result = result with
            {
                Payload = result.Payload.Substring(0, retainedLength) + trailer
            };
        }

        return result.ToEnvelopeText();
    }
}
