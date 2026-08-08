using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Recursively normalizes a caught <see cref="Exception"/> into a stable, evidence-safe
/// <see cref="VciProbeNormalizedExceptionInfo"/> tree: exception type name, message, and
/// <see cref="Exception.HResult"/> only. Stack traces and <see cref="Exception.Data"/> are
/// deliberately never read — <see cref="VciProbeNormalizedExceptionInfo"/> has no members capable
/// of carrying them. Follows <see cref="Exception.InnerException"/> to at most
/// <see cref="MaxInnerExceptionDepth"/> levels beyond the outer exception.
///
/// <para>
/// Shared by <see cref="VciProbeValueNormalizer"/> (path-canonicalization member-level failures)
/// and <see cref="VciProbeObservationRunner"/> (the <c>threw</c> case outcome) so exception
/// evidence is normalized exactly one way.
/// </para>
/// </summary>
public static class VciProbeExceptionNormalizer
{
    /// <summary>Maximum number of <see cref="Exception.InnerException"/> levels normalized beyond the outer exception.</summary>
    public const int MaxInnerExceptionDepth = 3;

    public static VciProbeNormalizedExceptionInfo Normalize(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        return NormalizeCore(exception, depth: 0);
    }

    private static VciProbeNormalizedExceptionInfo NormalizeCore(Exception exception, int depth)
    {
        var normalized = new VciProbeNormalizedExceptionInfo
        {
            ExceptionTypeName = exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
            HResult = exception.HResult,
        };

        if (exception.InnerException is not null && depth < MaxInnerExceptionDepth)
        {
            normalized.InnerException = NormalizeCore(exception.InnerException, depth + 1);
        }

        return normalized;
    }
}

/// <summary>
/// Recursively normalized exception evidence. Deliberately excludes the stack trace and
/// <see cref="Exception.Data"/> — see <see cref="VciProbeExceptionNormalizer"/>.
/// </summary>
public sealed class VciProbeNormalizedExceptionInfo
{
    /// <summary>Full CLR type name of the caught exception.</summary>
    public string ExceptionTypeName { get; set; } = string.Empty;

    /// <summary>Exception message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Exception HResult.</summary>
    public int HResult { get; set; }

    /// <summary>
    /// Normalized <see cref="Exception.InnerException"/>, when present and within
    /// <see cref="VciProbeExceptionNormalizer.MaxInnerExceptionDepth"/>.
    /// </summary>
    public VciProbeNormalizedExceptionInfo? InnerException { get; set; }
}

/// <summary>
/// Terminal outcome of exactly one pure observation-runner invocation. Exactly one of
/// <see cref="ReturnValue"/> (non-null, only when <see cref="Outcome"/> is <c>returned</c>),
/// <see cref="NotObservableReason"/> (only when <c>not_observable</c>), or
/// <see cref="Exception"/> (only when <c>threw</c>) is populated; all three are unset for
/// <c>returned_null</c>.
///
/// <para>
/// <see cref="Outcome"/> is always one of <c>returned</c>, <c>returned_null</c>,
/// <c>not_observable</c>, or <c>threw</c> — the four private-constructor factories below are the
/// only way to produce an instance, and none of them can produce <c>timed_out</c> or
/// <c>process_lost</c>. Those two <see cref="VciReadProbeContract.Outcomes"/> strings are reserved
/// for the live harness (Task 7/8), which observes transport/process state this pure worker-side
/// type never has access to.
/// </para>
/// </summary>
public sealed class VciProbeObservationOutcomeInfo
{
    private VciProbeObservationOutcomeInfo(
        string outcome,
        object? returnValue,
        string? notObservableReason,
        VciProbeNormalizedExceptionInfo? exception,
        bool isModifiedBefore,
        bool isModifiedAfter)
    {
        Outcome = outcome;
        ReturnValue = returnValue;
        NotObservableReason = notObservableReason;
        Exception = exception;
        IsModifiedBefore = isModifiedBefore;
        IsModifiedAfter = isModifiedAfter;
    }

    /// <summary>One of <c>returned</c>, <c>returned_null</c>, <c>not_observable</c>, or <c>threw</c>.</summary>
    public string Outcome { get; }

    /// <summary>The raw returned value. Populated only when <see cref="Outcome"/> is <c>returned</c>.</summary>
    public object? ReturnValue { get; }

    /// <summary>Explains why the case could not be observed. Populated only when <see cref="Outcome"/> is <c>not_observable</c>.</summary>
    public string? NotObservableReason { get; }

    /// <summary>Normalized exception evidence. Populated only when <see cref="Outcome"/> is <c>threw</c>.</summary>
    public VciProbeNormalizedExceptionInfo? Exception { get; }

    /// <summary>State-sampling delegate result immediately before the observed invocation (false/default for <c>not_observable</c>, which never invokes anything).</summary>
    public bool IsModifiedBefore { get; }

    /// <summary>State-sampling delegate result immediately after the observed invocation (false/default for <c>not_observable</c>, which never invokes anything).</summary>
    public bool IsModifiedAfter { get; }

    public static VciProbeObservationOutcomeInfo Returned(object value, bool before, bool after)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value), "Use ReturnedNull for a null return value.");
        }

        return new VciProbeObservationOutcomeInfo("returned", value, null, null, before, after);
    }

    public static VciProbeObservationOutcomeInfo ReturnedNull(bool before, bool after)
        => new("returned_null", null, null, null, before, after);

    public static VciProbeObservationOutcomeInfo NotObservable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("must be a nonblank string.", nameof(reason));
        }

        return new VciProbeObservationOutcomeInfo("not_observable", null, reason, null, false, false);
    }

    public static VciProbeObservationOutcomeInfo Threw(Exception exception, bool before, bool after)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        return new VciProbeObservationOutcomeInfo(
            "threw", null, null, VciProbeExceptionNormalizer.Normalize(exception), before, after);
    }
}

/// <summary>
/// Executes exactly one deliberate VCI member invocation and classifies its outcome, purely from a
/// caller-supplied read delegate and state-sampling delegate — no Siemens Openness dependency.
///
/// <para>
/// <paramref name="sampleState"/> (later tasks bind this to <c>() =&gt; project.IsModified</c>) is
/// sampled immediately before <paramref name="read"/> is invoked, and again in a <c>finally</c>
/// block that always runs after it — whether <paramref name="read"/> returned or threw. Only
/// <paramref name="read"/>'s own exceptions become <c>threw</c> evidence: an exception from
/// <paramref name="sampleState"/> itself (before or in the trailing <c>finally</c>) is never caught
/// here and propagates out of <see cref="Run"/> unchanged, exactly like a project-resolution or
/// request-validation failure elsewhere in the worker — never Siemens case evidence.
/// </para>
/// </summary>
public static class VciProbeObservationRunner
{
    public static VciProbeObservationOutcomeInfo Run(Func<bool> sampleState, Func<object?> read)
    {
        if (sampleState is null)
        {
            throw new ArgumentNullException(nameof(sampleState));
        }

        if (read is null)
        {
            throw new ArgumentNullException(nameof(read));
        }

        var before = sampleState();

        object? returnedValue = null;
        Exception? caught = null;
        var threw = false;
        bool after;

        try
        {
            try
            {
                returnedValue = read();
            }
            catch (Exception ex)
            {
                threw = true;
                caught = ex;
            }
        }
        finally
        {
            // Sampled unconditionally, whether the read above returned or threw. If sampleState
            // itself throws here, that exception is not caught anywhere in this method and
            // propagates out of Run() as an infrastructure failure, never a `threw` case outcome.
            after = sampleState();
        }

        if (threw)
        {
            return VciProbeObservationOutcomeInfo.Threw(caught!, before, after);
        }

        return returnedValue is null
            ? VciProbeObservationOutcomeInfo.ReturnedNull(before, after)
            : VciProbeObservationOutcomeInfo.Returned(returnedValue, before, after);
    }
}
