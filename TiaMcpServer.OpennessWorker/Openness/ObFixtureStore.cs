using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Loads the embedded V21 interface <c>Sections</c> element of an offered OB event class.</summary>
internal static class ObFixtureStore
{
  public static string LoadSections(string obEventClass)
  {
    if (!ObEventClasses.TryGet(obEventClass, out var cls))
    {
      throw ValidationFailure(
          $"Unknown OB event class '{obEventClass}'. Valid values: {ObEventClasses.NamesForMessage()}.");
    }

    var resourceName = $"TiaMcpServer.ObFixtures.{cls.Name}.xml";
    using var stream = typeof(ObFixtureStore).Assembly.GetManifestResourceStream(resourceName);
    if (stream is null)
    {
      throw ValidationFailure($"No interface fixture is embedded for OB event class '{cls.Name}'.");
    }

    using var reader = new StreamReader(stream);
    return reader.ReadToEnd().Trim();
  }

  private static WorkerOperationException ValidationFailure(string message)
  {
    return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
  }
}
