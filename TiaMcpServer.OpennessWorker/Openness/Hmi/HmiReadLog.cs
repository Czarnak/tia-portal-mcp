using Siemens.Engineering;

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

    /// <summary>Reads one property; an Openness failure becomes the default value, one message and an incomplete result.</summary>
    public T? Try<T>(Func<T> read, string what)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            Fail($"{what} could not be read: {ex.Message}");
            return default;
        }
    }
}
