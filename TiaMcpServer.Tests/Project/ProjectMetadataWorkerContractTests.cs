using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Project;

/// <summary>
/// Source-contract tests for the production <c>TiaMcpServer.OpennessWorker.Openness.ProjectMetadataReader</c>
/// and its wiring in <c>ProjectLifecycleService</c>. The worker cannot instantiate Siemens Openness
/// objects in an ordinary unit test (Openness uses .NET remoting that only works inside a real
/// TIA Portal-attached process), so these tests read the production source text and assert the
/// structural invariants the metadata contract depends on.
/// </summary>
public class ProjectMetadataWorkerContractTests
{
    private static string ReaderSource => File.ReadAllText(
        FindRepositoryFile("TiaMcpServer.OpennessWorker", "Openness", "ProjectMetadataReader.cs"));

    private static string LifecycleServiceSource => File.ReadAllText(
        FindRepositoryFile("TiaMcpServer.OpennessWorker", "Openness", "ProjectLifecycleService.cs"));

    [Fact]
    public void Reader_IsAReadOnlyServiceThatNeverMutatesProjectState()
    {
        var source = ReaderSource;

        Assert.DoesNotContain(".Save(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetAttribute", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Delete(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Close(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Open(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExclusiveAccess", source, StringComparison.Ordinal);
        Assert.DoesNotContain("project.Author =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_OnlyCatchesEngineeringExceptionNeverBroadBareCatches()
    {
        var source = ReaderSource;

        Assert.DoesNotContain("catch (Exception", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (SystemException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (ApplicationException", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"catch\s*\{"), source);

        var caughtTypes = Regex.Matches(source, @"catch\s*\(\s*([A-Za-z0-9_.]+)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(caughtTypes);
        Assert.All(caughtTypes, caughtType => Assert.Equal("EngineeringException", caughtType));
    }

    [Fact]
    public void Reader_EveryFailureDegradesToAWarningOnStderrNeverAFabricatedDefault()
    {
        var source = ReaderSource;

        // Every catch must surface a Console.Error warning so the degradation reaches the agent
        // through the captured-stderr warning channel instead of vanishing. Some sections issue a
        // warning for an unavailable-but-non-throwing value too, so writes must at least match
        // the number of approved, narrow catch blocks.
        Assert.True(
            CountOccurrences(source, "Console.Error.WriteLine")
                >= CountOccurrences(source, "catch (EngineeringException"));

        // And warnings must never be confused with hardcoded defaults: an unavailable V21
        // compilation setting must never be assigned a literal false.
        Assert.DoesNotContain("IsSimulationDuringBlockCompilationEnabled = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsVirtualPlcDuringBlockCompilationEnabled = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_PreservesAllMultilingualCommentTranslationsInSourceOrder()
    {
        var source = ReaderSource;
        Assert.Contains("item.Language?.Culture?.Name", source, StringComparison.Ordinal);
        Assert.Contains("Text = item.Text", source, StringComparison.Ordinal);
        Assert.Contains("translations.Add(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_ExposesLanguageSettingsViaCultureNamesAndNullableEditingReference()
    {
        var source = ReaderSource;
        Assert.Contains("languageSettings.Languages", source, StringComparison.Ordinal);
        Assert.Contains("languageSettings.ActiveLanguages", source, StringComparison.Ordinal);
        Assert.Contains("EditingLanguage", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceLanguage", source, StringComparison.Ordinal);
        Assert.Contains("language.Culture?.Name", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_CapsHistoryDeterministicallyAndFlagsTruncation()
    {
        var source = ReaderSource;

        Assert.Contains("public const int MaxHistoryEntries = 200;", source, StringComparison.Ordinal);
        Assert.Contains("entries.Count >= MaxHistoryEntries", source, StringComparison.Ordinal);
        Assert.Contains("truncated = true;", source, StringComparison.Ordinal);
        Assert.Contains("HistoryTruncated = truncated", source, StringComparison.Ordinal);

        // Once the first entry beyond the cap is detected, enumeration must STOP (break), never
        // keep walking the whole Openness history collection just to discard every remaining entry.
        var afterTruncated = source.Substring(
            source.IndexOf("truncated = true;", StringComparison.Ordinal));
        Assert.Contains("break;", afterTruncated, StringComparison.Ordinal);

        // The three-outcome contract: false (read, below cap), true (read, capped), null (failed).
        Assert.Contains("out bool? truncated", source, StringComparison.Ordinal);
        Assert.Contains("truncated = null;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_ExposesUsedProductsWithoutDeduplicationOrInference()
    {
        var source = ReaderSource;
        Assert.Contains("foreach (var product in project.UsedProducts)", source, StringComparison.Ordinal);
        Assert.Contains("Name = product.Name", source, StringComparison.Ordinal);
        Assert.Contains("Version = product.Version", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Distinct", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupBy", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_ReadsV21CompilationSettingsThroughGetServiceTreatingUnavailableAsNull()
    {
        var source = ReaderSource;
        Assert.Contains("GetService<PlcSimulationSettingsProvider>()", source, StringComparison.Ordinal);
        Assert.Contains("GetService<VirtualPlcSettingsProvider>()", source, StringComparison.Ordinal);
        Assert.Contains("IsSimulationDuringBlockCompilationEnabled", source, StringComparison.Ordinal);
        Assert.Contains("IsVirtualPlcDuringBlockCompilationEnabled", source, StringComparison.Ordinal);

        // An unavailable provider is reported as a warning and returns null - never a hardcoded false.
        Assert.Contains("was unavailable", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleService_AttachesMetadataOnlyToTheReadOnlyStatusRead()
    {
        var source = LifecycleServiceSource;

        // GetStatusReadOnly (the only get_project_status backing) attaches the full metadata.
        Assert.Contains("ReadStatusWithMetadata", source, StringComparison.Ordinal);
        Assert.Contains("status.Metadata = ProjectMetadataReader.Read(project);", source, StringComparison.Ordinal);

        // The write-side current-state probe (ProbeStatusForLifecycle) and the lifecycle
        // result/close payloads stay on plain ReadStatus, so their payloads and safety-token
        // binding are byte-for-byte unchanged.
        Assert.Contains("ProbeStatusForLifecycle", source, StringComparison.Ordinal);
        Assert.Contains("private static ProjectStatusInfo ReadStatus(Project project)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleService_PostWriteVerificationRead_IsBasicStatusOnly()
    {
        var source = LifecycleServiceSource;

        // Lifecycle post-write verification uses GetBasicStatusReadOnly - the same binding-policy
        // gate as GetStatusReadOnly but returning plain ReadStatus, so a write never enumerates
        // history, queries the V21 settings providers, or surfaces metadata warnings.
        Assert.Contains("GetBasicStatusReadOnly", source, StringComparison.Ordinal);
        Assert.Contains("public static ProjectStatusInfo GetBasicStatusReadOnly(TiaPortalSession session, string? requestedProjectPath)", source, StringComparison.Ordinal);

        // The metadata reader is invoked in exactly one place (ReadStatusWithMetadata, reachable
        // only from GetStatusReadOnly): the basic-status read and the lifecycle probe cannot
        // perform the extended metadata read.
        Assert.Equal(1, CountOccurrences(source, "ProjectMetadataReader.Read"));

        // The basic-status read must return plain ReadStatus, never the metadata-bearing variant.
        var basicStatusBody = source.Substring(
            source.IndexOf("public static ProjectStatusInfo GetBasicStatusReadOnly", StringComparison.Ordinal),
            source.IndexOf("private static Project? ResolveProjectForRead", StringComparison.Ordinal)
                - source.IndexOf("public static ProjectStatusInfo GetBasicStatusReadOnly", StringComparison.Ordinal));
        Assert.DoesNotContain("ProjectMetadataReader", basicStatusBody, StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleWriteTools_PostWriteVerification_UsesBasicStatusReadNotMetadataRead()
    {
        var writeToolsSource = File.ReadAllText(
            FindRepositoryFile("TiaMcpServer", "Tools", "ProjectWriteTools.cs"));
        var lifecycleToolsSource = File.ReadAllText(
            FindRepositoryFile("TiaMcpServer", "Tools", "ProjectLifecycleTools.cs"));

        // The five write tools (open/create/save/save-as/archive) verify with the basic-status
        // read only: ProjectWriteTools never calls GetProjectStatusAsync (the extended-metadata
        // read) at all; ProjectLifecycleTools calls it exactly once, and only in its direct-read
        // GetProjectStatus method - never in a post-write verification path. Each write tool uses
        // GetBasicProjectStatusAsync exactly once.
        Assert.Equal(0, CountOccurrences(writeToolsSource, "GetProjectStatusAsync"));
        Assert.Equal(1, CountOccurrences(lifecycleToolsSource, "GetProjectStatusAsync"));
        Assert.Equal(5, CountOccurrences(writeToolsSource, "GetBasicProjectStatusAsync"));
        Assert.Equal(5, CountOccurrences(lifecycleToolsSource, "GetBasicProjectStatusAsync"));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}