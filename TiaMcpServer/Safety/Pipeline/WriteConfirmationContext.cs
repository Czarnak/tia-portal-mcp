namespace TiaMcpServer.Safety.Pipeline;

/// <summary>Immutable startup options and the current call's client-specific elicitation adapter.</summary>
public sealed record WriteConfirmationContext(
    UserConfirmationOptions Options,
    UserConfirmation? Confirmation = null);
