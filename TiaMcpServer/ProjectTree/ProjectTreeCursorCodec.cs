using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;
using TiaMcpServer.Cursors;
using TiaMcpServer.Network;

namespace TiaMcpServer.ProjectTree;

internal sealed record ProjectTreeCursorState(
    string SnapshotId,
    string QueryHash,
    int Offset,
    ProjectBindingCursorState HostBinding);

internal sealed class ProjectTreeCursorException : Exception
{
    internal ProjectTreeCursorException(string category)
        : base("The supplied project-tree cursor is invalid.")
    {
        Category = category;
    }

    internal string Category { get; }
}

internal sealed class ProjectTreeCursorCodec
{
    private const string Purpose = "project-tree";
    private const int MaximumSnapshotIdChars = 128;
    private static readonly Regex LowercaseSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private readonly AuthenticatedCursorProtector _protector;

    internal ProjectTreeCursorCodec(AuthenticatedCursorProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    internal string Encode(ProjectTreeCursorState state)
    {
        try
        {
            Validate(state);
            return _protector.Protect(Purpose, state);
        }
        catch (Exception exception) when (exception is ArgumentException
            or AuthenticatedCursorException
            or AuthenticatedCursorSizeException
            or JsonException)
        {
            throw new ProjectTreeCursorException(WorkerFailureCategories.InvalidCursor);
        }
    }

    internal ProjectTreeCursorState Decode(string cursor)
    {
        try
        {
            var result = _protector.Unprotect<ProjectTreeCursorState>(Purpose, cursor);
            if (result.Status == AuthenticatedCursorStatus.ForeignProcess)
            {
                throw new ProjectTreeCursorException(WorkerFailureCategories.SnapshotUnavailable);
            }

            Validate(result.State);
            return result.State!;
        }
        catch (ProjectTreeCursorException)
        {
            throw;
        }
        catch (Exception exception) when (exception is AuthenticatedCursorException
            or ArgumentException
            or JsonException)
        {
            throw new ProjectTreeCursorException(WorkerFailureCategories.InvalidCursor);
        }
    }

    internal static bool IsValidSnapshotId(string? snapshotId)
        => !string.IsNullOrWhiteSpace(snapshotId) && snapshotId.Length <= MaximumSnapshotIdChars;

    internal static bool IsValidQueryHash(string? queryHash)
        => queryHash is { Length: 64 } && LowercaseSha256.IsMatch(queryHash);

    private static void Validate(ProjectTreeCursorState? state)
    {
        if (state is null
            || !IsValidSnapshotId(state.SnapshotId)
            || !IsValidQueryHash(state.QueryHash)
            || state.Offset < 0
            || state.HostBinding is null
            || !IsValidHostBinding(state.HostBinding))
        {
            throw new JsonException("Project-tree cursor payload values are invalid.");
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

        var path = ProjectPathNormalization.Canonicalize(binding.NormalizedProjectPath);
        return !string.IsNullOrWhiteSpace(binding.BindingId)
            && binding.Revision is >= 0
            && path is not null
            && string.Equals(binding.NormalizedProjectPath, path, StringComparison.OrdinalIgnoreCase);
    }
}
