using Siemens.Engineering;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Collects the <c>messages</c> and <c>isComplete</c> of one hmi_read result. <see cref="Note"/> records a
/// fact that does not make the result incomplete (a sub-object a device does not have);
/// <see cref="Fail"/> records a property that could not be read.
/// </summary>
internal sealed class HmiReadLog
{
    public List<string> Messages { get; } = new();

    public bool IsComplete { get; private set; } = true;

    public void Note(string message) => Messages.Add(message);

    public void Fail(string message)
    {
        Messages.Add(message);
        IsComplete = false;
    }

    /// <summary>
    /// Reads something the whole item depends on (a name or a composition); any Openness failure fails the
    /// item as <c>worker_operation_failed</c> naming <paramref name="what"/>.
    /// </summary>
    public static T Guard<T>(Func<T> read, string what, bool describe = false)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed, $"{what} could not be read: {ex.Message}",
                engineeringError: describe ? WorkerExecution.Describe(ex) : null);
        }
    }

    /// <summary>Reads one property; an Openness failure becomes the default value, one message and an incomplete result.</summary>
    public T? Try<T>(Func<T> read, string what)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            Recover(ex, $"{what} could not be read: {ex.Message}");
            return default;
        }
    }

    /// <summary>
    /// Like <see cref="Try{T}"/>, for a value Openness may return as null where one is expected (a sub-object):
    /// null is one message and an incomplete result (D8) instead of a later <c>NullReferenceException</c>.
    /// </summary>
    public T? TryPresent<T>(Func<T?> read, string what)
        where T : class
    {
        var before = Messages.Count;
        var value = Try(read, what);
        if (value is null && Messages.Count == before)
        {
            Fail($"{what} is null.");
        }

        return value;
    }

    /// <summary>
    /// A continuation reads its held objects live (spec §4.4): one deleted since the first page throws
    /// <see cref="EngineeringObjectDisposedException"/> on its name and fails only its row. A fresh scan has just
    /// read every name, so it needs no probe; any other failure is left to the row read.
    /// </summary>
    public bool IsLive(HmiPaging paging, object held, string what)
    {
        if (paging.Snapshot is null || held is not IEngineeringObject engineeringObject)
        {
            return true;
        }

        try
        {
            engineeringObject.GetAttribute("Name");
            return true;
        }
        catch (EngineeringObjectDisposedException)
        {
            Fail($"{what} no longer exists; it was deleted after the first page was read.");
            return false;
        }
        catch (EngineeringException)
        {
            return true;
        }
    }

    /// <summary>
    /// Openness reports an unsupported property on the current device as <see cref="EngineeringTargetInvocationException"/>
    /// or <see cref="EngineeringNotSupportedException"/>; those are recoverable per property. Anything else
    /// (a disposed object after the Portal died, a security failure) fails the whole item.
    /// </summary>
    public static bool IsRecoverable(Exception ex)
        => ex is EngineeringTargetInvocationException or EngineeringNotSupportedException;

    /// <summary>Records a recoverable failure; rethrows anything else as <c>worker_operation_failed</c>.</summary>
    public void Recover(EngineeringException ex, string message)
    {
        if (!IsRecoverable(ex))
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, message);
        }

        Fail(message);
    }

    /// <summary>The raw per-culture texts, or null (plus a message) when the property could not be read; a skipped item makes the result incomplete.</summary>
    public List<HmiText>? ReadText(Func<MultilingualText?> read, string? language, string what)
    {
        MultilingualText? text;
        try
        {
            text = read();
        }
        catch (EngineeringException ex)
        {
            Recover(ex, $"{what} could not be read: {ex.Message}");
            return null;
        }

        var skipped = new List<string>();
        var texts = HmiTextMapper.Map(text, language, skipped).ToList();
        foreach (var message in skipped)
        {
            Fail(message);
        }

        return texts;
    }

    public HmiVariant? ReadVariant(Func<object?> read, string what)
    {
        object? value;
        try
        {
            value = read();
        }
        catch (EngineeringException ex)
        {
            Recover(ex, $"{what} could not be read: {ex.Message}");
            return null;
        }

        return HmiVariantMapper.Map(value);
    }
}
