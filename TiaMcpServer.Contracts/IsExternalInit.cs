namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill required for C# `init` accessors to compile under netstandard2.0, which does not
/// ship this marker type in its BCL (net8.0 and net48 both provide/tolerate it already). The
/// compiler only needs the type to exist and be resolvable within this compilation; it is never
/// otherwise used at runtime. See <see cref="TiaMcpServer.Contracts.WorkerResponse.FailureCategory"/>,
/// the first `init`-only property in this assembly.
/// </summary>
internal static class IsExternalInit
{
}
