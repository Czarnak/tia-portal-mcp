using TiaMcpServer.Batch;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

internal static class StandaloneToolResultFormatter
{
    public static string Format(WorkerCallResult result, string narrowingHint)
    {
        if (result.Success && result.Payload.Length > BatchPayloadBudget.MaxItemChars)
        {
            var trailer = $"\n[TRUNCATED — payload exceeded "
                + $"{BatchPayloadBudget.MaxItemChars} characters. {narrowingHint}]";
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