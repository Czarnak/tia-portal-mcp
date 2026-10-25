namespace Siemens.Engineering;

// Source-linked Network helpers need the exception type, never a Siemens runtime assembly.
internal sealed class NonRecoverableException : Exception;
internal class EngineeringException(string message) : Exception(message);
internal sealed class EngineeringTargetInvocationException(string message) : EngineeringException(message);
