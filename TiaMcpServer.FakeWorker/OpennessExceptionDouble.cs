namespace Siemens.Engineering;

// Source-linked Network helpers need the exception type, never a Siemens runtime assembly.
internal sealed class NonRecoverableException : Exception;
