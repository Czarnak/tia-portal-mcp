namespace TiaMcpServer.Safety.Pipeline;

/// <summary>
/// Immutable startup configuration for the Phase 2 elicitation gate.
/// Phase 1b registers this setting without changing legacy preview/apply behavior.
/// </summary>
public sealed record UserConfirmationOptions(bool ConfirmWithUser = true);
