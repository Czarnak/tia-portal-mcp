using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Every field an operation declares must actually reach the worker. Two live instances of the
/// opposite — operation-specific optional values — can otherwise be silently discarded because
/// nothing checks the catalog-to-worker boundary. Asserts by VALUE, not
/// by property name, so renaming either side of the boundary cannot make this pass vacuously.
/// </summary>
public class BatchFieldForwardingTests
{
    private static string LocateFakeWorker()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "TiaMcpServer.FakeWorker", "bin", configuration, "net8.0",
                    "TiaMcpServer.FakeWorker.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            directory = directory.Parent!;
        }

        throw new FileNotFoundException("TiaMcpServer.FakeWorker.exe not found; build the solution first.");
    }

    private static readonly Dictionary<string, object> ValidatedFieldValues = new(StringComparer.Ordinal)
    {
        ["filter"] = "UnusedObjects",
        ["blockType"] = "FB",
        ["language"] = "SCL",
        ["obEventClass"] = "CyclicInterrupt",
        ["dataType"] = "Bool",
        ["depth"] = 7,
        ["maxResults"] = 4242,
    };

    private static object SentinelFor(PropertyInfo property, string fieldName, string operationName)
    {
        if (fieldName == "format")
        {
            // NormalizeFormat supplies xml for block operations and source for type operations,
            // so use the opposite valid value to prove the caller supplied it.
            return operationName is "get_block_content" or "update_block_logic"
                ? SourceFormatNames.Source
                : SourceFormatNames.Xml;
        }

        if (ValidatedFieldValues.TryGetValue(fieldName, out var known))
        {
            return known;
        }

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(int))
        {
            return 4243;
        }

        return $"__sentinel_{fieldName}__";
    }

    private static (BatchOperationRequest Request, List<object> Expected) Build(BatchOperationSpec spec)
    {
        var request = new BatchOperationRequest
        {
            OperationId = "item-1",
            Operation = spec.Name,
            ProjectPath = "echo"
        };

        var expected = new List<object>();
        foreach (var fieldName in spec.RequiredFields.Concat(spec.OptionalFields))
        {
            var propertyName = char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1);
            var property = typeof(BatchOperationRequest).GetProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Spec '{spec.Name}' declares field '{fieldName}' with no matching BatchOperationRequest property.");

            var value = SentinelFor(property, fieldName, spec.Name);
            property.SetValue(request, value);
            expected.Add(value);
        }

        return (request, expected);
    }

    private static string RenderValue(object value) => value switch
    {
        bool b => b ? "true" : "false",
        int i => i.ToString(),
        _ => JsonSerializer.Serialize(value).Trim('"')
    };

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static BatchOperationRequest BuildBaseline(BatchOperationSpec spec) => new()
    {
        OperationId = "baseline",
        Operation = spec.Name,
        ProjectPath = "echo"
    };

    public static TheoryData<string> AllOperations()
    {
        var data = new TheoryData<string>();
        foreach (var spec in BatchOperationCatalog.All)
        {
            data.Add(spec.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task EveryDeclaredField_ReachesTheWorker(string operationName)
    {
        Assert.True(BatchOperationCatalog.TryGetSpec(operationName, out var spec));
        var (request, expected) = Build(spec!);

        using var client = new OpennessWorkerClient(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: LocateFakeWorker());

        var result = await BatchWorkerInvoker.InvokeAsync(client, request);

        Assert.True(result.Success, result.Error);

        var groups = expected.Select(RenderValue).GroupBy(value => value, StringComparer.Ordinal).ToList();
        var baselineResult = await BatchWorkerInvoker.InvokeAsync(client, BuildBaseline(spec!));
        Assert.True(baselineResult.Success, baselineResult.Error);
        var baselinePayload = baselineResult.Payload;

        foreach (var group in groups)
        {
            var actualCount = CountOccurrences(result.Payload, group.Key)
                - CountOccurrences(baselinePayload, group.Key);
            Assert.True(
                actualCount == group.Count(),
                $"Operation '{operationName}' expected {group.Count()} occurrences of value "
                + $"'{group.Key}' but received {actualCount}. Echoed request: {result.Payload}");
        }
    }

    [Fact]
    public void EveryDeclaredField_HasAMatchingRequestProperty()
    {
        foreach (var spec in BatchOperationCatalog.All)
        {
            foreach (var fieldName in spec.RequiredFields.Concat(spec.OptionalFields))
            {
                var propertyName = char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1);
                Assert.NotNull(typeof(BatchOperationRequest).GetProperty(propertyName));
            }
        }
    }
}
