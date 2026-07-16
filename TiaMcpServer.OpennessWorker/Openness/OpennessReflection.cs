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
