using System.Collections;
using System.Reflection;
using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Exception-guarded reflection helpers for reading properties and collections off TIA Openness
/// objects whose getters may throw <see cref="EngineeringException"/> at runtime. Failures are logged
/// to stderr and degrade to null / an empty sequence so a single bad member does not abort a read.
/// </summary>
/// <remarks>
/// Uses the narrow <see cref="EngineeringException"/> guard. Reflection over unverified SDK surfaces
/// uses the description-taking ReadProperty overload below.
/// </remarks>
internal static class OpennessReflection
{
    public static object? ReadProperty(object? instance, string propertyName)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            return instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(instance);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is EngineeringException engineeringException)
        {
            Console.Error.WriteLine($"Skipping property '{propertyName}': {engineeringException.Message}");
            return null;
        }
    }

    /// <summary>
    /// Broad-but-bounded variant for reflection over unverified SDK surfaces: additionally
    /// swallows <see cref="TargetInvocationException"/> regardless of inner type. Anything
    /// else (e.g. AmbiguousMatchException) is a bug and must propagate.
    /// </summary>
    public static object? ReadProperty(object? instance, string propertyName, string description)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            return instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(instance);
        }
        catch (TargetInvocationException ex)
        {
            Console.Error.WriteLine($"Skipping {description}: {ex.InnerException?.Message ?? ex.Message}");
            return null;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping {description}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads a member that may be a CLR property on some Openness types and only a dynamic
    /// Openness attribute on others (for example <c>Subnet.SubnetId</c>, which
    /// <c>GetAttributeInfos()</c> lists as a <c>[ReadWrite]</c> attribute rather than exposing as a
    /// property). Tries the CLR property first, then falls back to <c>GetAttribute</c>.
    ///
    /// <para>
    /// <c>GetAttribute</c> with an unsupported attribute name throws rather than returning null, so
    /// the fallback is guarded the same way <see cref="ReadProperty(object?, string)"/> already
    /// guards its own reflection: an <see cref="EngineeringException"/> degrades to null instead of
    /// propagating. Callers resolving an identity a write selector will match against should treat a
    /// null result the same as "this candidate's identity is unreadable" — it must never satisfy a
    /// selector.
    /// </para>
    /// </summary>
    public static string? ReadPropertyOrAttribute(object instance, string propertyName)
    {
        var value = ReadProperty(instance, propertyName);
        if (value is not null)
        {
            return value.ToString();
        }

        if (instance is not IEngineeringObject engineeringObject)
        {
            return null;
        }

        try
        {
            return engineeringObject.GetAttribute(propertyName)?.ToString();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping attribute '{propertyName}': {ex.Message}");
            return null;
        }
    }

    public static IEnumerable<object> ReadEnumerableProperty(object instance, string propertyName)
    {
        return Enumerate(ReadProperty(instance, propertyName), propertyName);
    }

    public static IEnumerable<object> ReadEnumerableProperty(object instance, string propertyName, string description)
    {
        return Enumerate(ReadProperty(instance, propertyName), description);
    }

    public static IEnumerable<object> Enumerate(object? collection, string description)
    {
        if (collection is null)
        {
            yield break;
        }

        if (collection is string)
        {
            yield break;
        }

        if (collection is not IEnumerable enumerable)
        {
            yield break;
        }

        IEnumerator enumerator;
        try
        {
            enumerator = enumerable.GetEnumerator();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping {description}: {ex.Message}");
            yield break;
        }

        while (true)
        {
            object? current;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                current = enumerator.Current;
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping an entry while reading {description}: {ex.Message}");
                yield break;
            }

            if (current is not null)
            {
                yield return current;
            }
        }
    }
}
