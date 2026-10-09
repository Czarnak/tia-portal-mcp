using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Cursors;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Network;

internal sealed class HardwarePageCursorException : Exception
{
    internal HardwarePageCursorException()
        : base("The supplied hardware page cursor is invalid.")
    {
    }

    internal string Category => WorkerFailureCategories.InvalidCursor;
}

internal sealed class HardwarePageCursorCodec
{
    private const int CurrentVersion = 1;
    private const string Purpose = "hardware-page";
    private static readonly Regex LowercaseSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private readonly AuthenticatedCursorProtector _protector;

    internal HardwarePageCursorCodec(AuthenticatedCursorProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    internal HardwarePageCursorCodec(byte[] key)
        : this(new AuthenticatedCursorProtector(key, "hardware-page-compatibility"))
    {
    }

    internal string Encode(HardwarePageCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);
        return _protector.Protect(Purpose, state);
    }

    internal HardwarePageCursorState Decode(string cursor)
    {
        try
        {
            var result = _protector.Unprotect<HardwarePageCursorState>(Purpose, cursor);
            if (result.Status != AuthenticatedCursorStatus.Success || result.State is null)
            {
                throw new HardwarePageCursorException();
            }

            ValidateState(result.State);
            return result.State;
        }
        catch (Exception exception) when (exception is AuthenticatedCursorException
            or ArgumentException
            or JsonException)
        {
            throw new HardwarePageCursorException();
        }
    }

    private static void ValidateState(HardwarePageCursorState state)
    {
        var normalizedResolvedPath = ProjectPathNormalization.Canonicalize(state.ResolvedProjectPath);
        if (state.Version != CurrentVersion
            || normalizedResolvedPath is null
            || !string.Equals(state.ResolvedProjectPath, normalizedResolvedPath, StringComparison.OrdinalIgnoreCase)
            || state.SessionIdentity is null
            || string.IsNullOrWhiteSpace(state.SessionIdentity.WorkerSessionId)
            || state.SessionIdentity.SessionGeneration < 0
            || state.SessionIdentity.PortalProcessId is null
            || state.SessionIdentity.PortalProcessId <= 0
            || !SameProject(state.SessionIdentity.ProjectPath, state.ResolvedProjectPath)
            || state.HostBinding is null
            || !IsValidHostBinding(state.HostBinding)
            || (state.HostBinding.IsBound
                && !SameProject(state.HostBinding.NormalizedProjectPath, state.ResolvedProjectPath))
            || !LowercaseSha256.IsMatch(state.QueryHash ?? string.Empty)
            || state.OrderingVersion <= 0
            || !LowercaseSha256.IsMatch(state.SnapshotHash ?? string.Empty)
            || state.Offset < 0)
        {
            throw new JsonException("Cursor payload values are invalid.");
        }
    }

    private static bool IsValidHostBinding(ProjectBindingCursorState binding)
    {
        if (!binding.IsBound)
        {
            return binding.BindingId is null
                && binding.Revision is null
                && binding.NormalizedProjectPath is null;
        }

        var normalizedPath = ProjectPathNormalization.Canonicalize(binding.NormalizedProjectPath);
        return !string.IsNullOrWhiteSpace(binding.BindingId)
            && binding.Revision is >= 0
            && normalizedPath is not null
            && string.Equals(binding.NormalizedProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameProject(string? first, string? second)
    {
        var normalizedFirst = ProjectPathNormalization.Canonicalize(first);
        var normalizedSecond = ProjectPathNormalization.Canonicalize(second);
        return normalizedFirst is not null
            && normalizedSecond is not null
            && string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class HardwarePageCursorValidator
{
    internal static string? Validate(
        HardwarePageCursorState state,
        NetworkOperationRequest request,
        ProjectBindingSnapshot currentBinding)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentBinding);

        var incomingQueryHash = HardwarePageEvidence.CreateQueryHash(
            request.DeviceName,
            request.PlcName,
            request.IncludeIoDetails,
            request.IncludeTagMatches);
        if (!string.Equals(state.QueryHash, incomingQueryHash, StringComparison.Ordinal))
        {
            return WorkerFailureCategories.CursorFilterMismatch;
        }

        if (request.ProjectPath is not null
            && !SameProject(request.ProjectPath, state.ResolvedProjectPath))
        {
            return WorkerFailureCategories.CursorBindingMismatch;
        }

        return state.HostBinding.Matches(currentBinding)
            ? null
            : WorkerFailureCategories.CursorBindingMismatch;
    }

    private static bool SameProject(string? first, string? second)
    {
        var normalizedFirst = ProjectPathNormalization.Canonicalize(first);
        var normalizedSecond = ProjectPathNormalization.Canonicalize(second);
        return normalizedFirst is not null
            && normalizedSecond is not null
            && string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }
}
