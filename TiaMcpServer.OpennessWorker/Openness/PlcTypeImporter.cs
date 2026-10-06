using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.SW.ExternalSources;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Updates one existing PlcType from Siemens external-source text (.udt) or Simatic ML.
///
/// <para>
/// Strictly an update. Two refusals come before any project mutation: the type must already exist,
/// and the name the submitted document declares must match the type the path resolved to. Openness'
/// GenerateBlocksFromSource has no notion of a target object — it creates whatever the source
/// declares — so without those refusals a typo in the path would silently add a stray type instead
/// of failing.
/// </para>
/// </summary>
internal static class PlcTypeImporter
{
    public static PlcTypeImportResultInfo Import(
        Project project,
        string typePath,
        string sourceContent,
        string format)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (sourceContent is null) throw new ArgumentNullException(nameof(sourceContent));

        // 1. Parse the path and resolve the target.
        var address = PlcTypeAddress.Parse(typePath);
        var target = PlcTypeTargetResolver.ResolveForImport(project, address);

        // 2. Refuse if the type does not exist. This is an update, never an upsert.
        if (target.Type is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No PLC data type exists at '{address.ToDisplayPath()}'. update_type_content only "
                + "updates a type that is already in the project; it never creates one.");
        }

        var targetName = target.Type.Name;

        // 3. Refuse if the submitted document declares a different object.
        if (!PlcTypeSourcePreflight.TryReadDeclaredName(
                sourceContent, format, SourceObjectKind.Type, out var declaredName, out var preflightError))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                preflightError ?? "The submitted document declares no object name.");
        }

        if (!string.Equals(declaredName, targetName, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"The submitted document declares '{declaredName}' but '{address.ToDisplayPath()}' "
                + $"resolves to the PLC data type '{targetName}'. update_type_content never renames "
                + $"and never creates: submit a document declaring '{targetName}', or address the "
                + "type the document actually declares.");
        }

        // 4/5. Apply the document.
        var outcome = string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal)
            ? ImportXml(target, sourceContent)
            : ImportSource(target, targetName, sourceContent);

        // 6. Verify, carrying whether the temporary project node made it back out.
        var evidence = PlcTypePostconditionVerifier.BuildEvidence(project, address, format, outcome.ProjectNodeRemoved);
        PlcTypePostconditionVerifier.Verify(evidence);

        return new PlcTypeImportResultInfo
        {
            Operation = "update_type_content",
            TypePath = address.ToDisplayPath(),
            TypeName = targetName,
            Format = format,
            ProjectNodeRemoved = outcome.ProjectNodeRemoved,
            GeneratedObjectCount = outcome.GeneratedObjectCount
        };
    }

    /// <summary>
    /// Simatic ML goes straight into the resolved group's own composition — no external source node
    /// is created, so there is nothing that could survive in the project.
    /// </summary>
    private static ImportOutcome ImportXml(ResolvedTypeTarget target, string sourceContent)
    {
        var stagingDirectory = Path.Combine(
            Path.GetTempPath(), "tia-mcp-type-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var path = Path.Combine(stagingDirectory, target.DocumentName + ".xml");
            File.WriteAllText(path, sourceContent, Encoding.UTF8);

            var imported = target.Group.Types.Import(new FileInfo(path), ImportOptions.Override);
            return new ImportOutcome(projectNodeRemoved: true, imported?.Count ?? 0);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    /// <summary>
    /// External-source text goes through the Siemens pipeline: register the file as a
    /// PlcExternalSource under the owning software scope's group, generate from it, then remove the
    /// node again. The scope owns both halves of that cleanup and is disposed on every path,
    /// including a throwing GenerateBlocksFromSource.
    /// </summary>
    private static ImportOutcome ImportSource(
        ResolvedTypeTarget target,
        string targetName,
        string sourceContent)
    {
        // target.ExternalSourceGroup, not one re-derived from the type: for a type inside a
        // software unit this is the unit's own group, and registering under the top-level PLC
        // instead would generate a stray type there and leave the real one untouched.
        var scope = ExternalSourceScope.Create(
            target.ExternalSourceGroup, targetName + ".udt", sourceContent);

        IList<IEngineeringObject> generated;

        try
        {
            generated = target.UserGroup is not null
                ? scope.Source.GenerateBlocksFromSource(target.UserGroup, GenerateBlockOption.None)
                // The Types root is a PlcTypeSystemGroup, not a user group, so there is no group to
                // pass. GenerateBlockOption.None is still passed explicitly: the truly parameterless
                // overload leaves the on-error behaviour implicit, and both branches must refuse to
                // keep partially generated objects.
                : scope.Source.GenerateBlocksFromSource(GenerateBlockOption.None);
        }
        finally
        {
            scope.Dispose();
        }

        return new ImportOutcome(scope.ProjectNodeRemoved, generated?.Count ?? 0);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // Cleanup must never replace the outcome of the import itself. stderr is captured into
            // the response warnings, so a systematically failing cleanup still surfaces.
            Console.Error.WriteLine($"PLC data type import staging cleanup failed: {ex.Message}");
        }
    }

    /// <summary>What one applied document did, before postcondition verification runs.</summary>
    private readonly struct ImportOutcome
    {
        public ImportOutcome(bool projectNodeRemoved, int generatedObjectCount)
        {
            ProjectNodeRemoved = projectNodeRemoved;
            GeneratedObjectCount = generatedObjectCount;
        }

        public bool ProjectNodeRemoved { get; }

        public int GeneratedObjectCount { get; }
    }
}
