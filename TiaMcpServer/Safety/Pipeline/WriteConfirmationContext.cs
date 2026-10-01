namespace TiaMcpServer.Safety.Pipeline;

/// <summary>The current call's client-specific elicitation adapter.</summary>
public sealed record WriteConfirmationContext(UserConfirmation? Confirmation);
