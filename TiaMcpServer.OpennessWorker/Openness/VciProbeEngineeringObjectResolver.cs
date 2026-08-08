using System;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Settings;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Resolves a <see cref="VciEngineeringObjectSelectorInfo"/> back to a live Siemens engineering
/// object for the VCI Workspace Phase 1 read-only probe. Always re-runs
/// <see cref="VciProbeEngineeringObjectCatalog.Enumerate"/> for the current project rather than
/// trusting a previously cached candidate — a selector may have been captured in an earlier worker
/// request (a different process), so nothing here may assume the object it names is still the same
/// one, still exists, or still lives at the same structural path.
///
/// <para>
/// Prefers <see cref="VciEngineeringObjectSelectorInfo.StableIdentifier"/> via
/// <c>ObjectIdentifierProvider.Find</c> when present; otherwise falls back to matching
/// <see cref="VciEngineeringObjectSelectorInfo.StructuralPath"/> against the freshly discovered
/// candidates. Either way, the match is re-verified against its freshly recomputed fingerprint
/// before <see cref="Resolve"/> reports it resolved — a selector that resolves to the wrong runtime
/// type or a structurally moved/renamed object reports
/// <see cref="NotObservableReasons.SelectorStaleOrAmbiguous"/> rather than picking a
/// best-effort match by name alone.
/// </para>
/// </summary>
public static class VciProbeEngineeringObjectResolver
{
    public static VciProbeEngineeringObjectResolution Resolve(
        Project project,
        VciProbeRequestInfo request,
        VciEngineeringObjectSelectorInfo selector)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        var catalog = VciProbeEngineeringObjectCatalog.Enumerate(project, request);

        VciProbeEngineeringObjectCandidate? candidate = null;

        if (!string.IsNullOrWhiteSpace(selector.StableIdentifier))
        {
            var foundById = ResolveByStableIdentifier(project, selector.StableIdentifier!);
            if (foundById is not null)
            {
                candidate = catalog.Candidates.FirstOrDefault(c => Equals(c.EngineeringObject, foundById));
            }
        }

        candidate ??= FindStructuralMatch(catalog.Candidates, selector.StructuralPath);

        if (candidate is null)
        {
            return VciProbeEngineeringObjectResolution.NotObservable(NotObservableReasons.SelectorStaleOrAmbiguous);
        }

        // Verify runtime type and fingerprint before the caller ever invokes anything on the
        // candidate: a freshly recomputed fingerprint that disagrees with the selector's stored one
        // means the object moved, was renamed, or was replaced by something of a different runtime
        // type at the same structural position since the selector was captured.
        if (string.IsNullOrWhiteSpace(selector.Fingerprint)
            || !string.Equals(candidate.Fingerprint, selector.Fingerprint, StringComparison.Ordinal))
        {
            return VciProbeEngineeringObjectResolution.NotObservable(NotObservableReasons.SelectorStaleOrAmbiguous);
        }

        return VciProbeEngineeringObjectResolution.Resolved(candidate);
    }

    private static object? ResolveByStableIdentifier(Project project, string stableIdentifier)
    {
        try
        {
            var idProvider = project.GetService<ObjectIdentifierProvider>();
            return idProvider?.Find(stableIdentifier);
        }
        catch (EngineeringException)
        {
            // Unsupported identifier shape, disposed provider, or an identifier that no longer
            // resolves to anything — all fall back to the structural-path match below.
            return null;
        }
    }

    private static VciProbeEngineeringObjectCandidate? FindStructuralMatch(
        System.Collections.Generic.List<VciProbeEngineeringObjectCandidate> candidates,
        System.Collections.Generic.List<VciEngineeringObjectPathSegmentInfo> structuralPath)
    {
        if (structuralPath is null || structuralPath.Count == 0)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (StructuralPathsMatch(candidate.Selector.StructuralPath, structuralPath))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool StructuralPathsMatch(
        System.Collections.Generic.List<VciEngineeringObjectPathSegmentInfo> candidatePath,
        System.Collections.Generic.List<VciEngineeringObjectPathSegmentInfo> requestedPath)
    {
        if (candidatePath.Count != requestedPath.Count)
        {
            return false;
        }

        for (var i = 0; i < candidatePath.Count; i++)
        {
            var a = candidatePath[i];
            var b = requestedPath[i];

            if (a.Index != b.Index
                || !string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                || !string.Equals(a.ObjectType, b.ObjectType, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Closed vocabulary of <c>not_observable</c> reasons this resolver may report.</summary>
public static class NotObservableReasons
{
    /// <summary>
    /// The selector's stable identifier and structural path both failed to resolve to a candidate
    /// with a matching fingerprint — the underlying object may have moved, been renamed, been
    /// deleted, or been replaced by something of a different runtime type since the selector was
    /// captured. Never resolved by falling back to a name-only match.
    /// </summary>
    public const string SelectorStaleOrAmbiguous = "selector_stale_or_ambiguous";
}

/// <summary>Terminal outcome of one <see cref="VciProbeEngineeringObjectResolver.Resolve"/> call.</summary>
public sealed class VciProbeEngineeringObjectResolution
{
    private VciProbeEngineeringObjectResolution(VciProbeEngineeringObjectCandidate? candidate, string? notObservableReason)
    {
        Candidate = candidate;
        NotObservableReason = notObservableReason;
    }

    /// <summary>True when a candidate was resolved and its fingerprint verified.</summary>
    public bool IsResolved => Candidate is not null;

    /// <summary>The resolved candidate. Populated only when <see cref="IsResolved"/> is <see langword="true"/>.</summary>
    public VciProbeEngineeringObjectCandidate? Candidate { get; }

    /// <summary>One of <see cref="NotObservableReasons"/>. Populated only when <see cref="IsResolved"/> is <see langword="false"/>.</summary>
    public string? NotObservableReason { get; }

    public static VciProbeEngineeringObjectResolution Resolved(VciProbeEngineeringObjectCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        return new VciProbeEngineeringObjectResolution(candidate, null);
    }

    public static VciProbeEngineeringObjectResolution NotObservable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("must be a nonblank string.", nameof(reason));
        }

        return new VciProbeEngineeringObjectResolution(null, reason);
    }
}
