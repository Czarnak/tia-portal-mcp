using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Every field an operation declares must actually reach the worker. Two live instances of the
/// opposite — deviceItemName on configure_network_device, and the external* attributes on
/// create_tag — were silently discarded for months because nothing checked. Asserts by VALUE, not
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

    private static object SentinelFor(PropertyInfo property, string fieldName)
    {
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

            var value = SentinelFor(property, fieldName);
            property.SetValue(request, value);
            expected.Add(value);
        }

        return (request, expected);
    }

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

        foreach (var value in expected)
        {
            var rendered = value switch
            {
                bool b => b ? "true" : "false",
                int i => i.ToString(),
                _ => JsonSerializer.Serialize(value).Trim('"')
            };

            Assert.True(
                result.Payload.Contains(rendered, StringComparison.Ordinal),
                $"Operation '{operationName}' declares a field whose value '{rendered}' never reached "
                + $"the worker. Echoed request: {result.Payload}");
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
