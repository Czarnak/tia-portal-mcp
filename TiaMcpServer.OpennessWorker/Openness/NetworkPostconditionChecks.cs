using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Siemens-free comparison; unreadable state can never establish a postcondition.</summary>
internal static class NetworkPostconditionChecks
{
    public static NetworkVerificationCheckInfo Compare(string name, string? expected, string? observed, bool readable)
    {
        var status = !readable ? "unverified"
            : string.Equals(expected, observed, StringComparison.Ordinal) ? "passed" : "failed";
        return new NetworkVerificationCheckInfo
        {
            Name = name, Expected = expected, Observed = readable ? observed : null, Status = status,
            Message = status == "passed" ? null : "Immediate post-read " + (readable ? "did not match." : "was unavailable.")
                + " Inspect the project before retrying.",
        };
    }

    public static NetworkMutationVerificationInfo Complete(NetworkMutationVerificationInfo evidence)
    {
        evidence.Status = evidence.Checks.Any(check => check.Status == "failed") ? "failed"
            : evidence.Checks.Any(check => check.Status == "unverified") ? "unverified"
            : evidence.Checks.Count == 0 ? "not_required" : "passed";
        evidence.Message = evidence.Status == "failed" || evidence.Status == "unverified"
            ? "The mutation may have taken effect. Inspect current state before retrying." : null;
        return evidence;
    }
}
