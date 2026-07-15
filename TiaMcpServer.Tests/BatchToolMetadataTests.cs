using System.ComponentModel;
using System.Linq;
using System.Reflection;
using TiaMcpServer.Batch;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchToolMetadataTests
{
    private static string MethodDescription(string methodName)
    {
        var method = typeof(BatchTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        return description!.Description;
    }

    [Fact]
    public void ExecuteReadBatchDescription_ListsEveryReadOperation()
    {
        var description = MethodDescription("ExecuteReadBatch");
        foreach (var operation in BatchOperationCatalog.ReadOperationNames)
        {
            Assert.Contains(operation, description);
        }
    }

    [Fact]
    public void PreviewWriteBatchDescription_ListsEveryWriteOperation()
    {
        var description = MethodDescription("PreviewWriteBatch");
        foreach (var operation in BatchOperationCatalog.WriteOperationNames)
        {
            Assert.Contains(operation, description);
        }
    }

    [Fact]
    public void ApplyWriteBatchDescription_ListsEveryWriteOperation()
    {
        var description = MethodDescription("ApplyWriteBatch");
        foreach (var operation in BatchOperationCatalog.WriteOperationNames)
        {
            Assert.Contains(operation, description);
        }
    }

    [Fact]
    public void OperationPropertyDescription_ListsEveryBatchableOperation()
    {
        var property = typeof(BatchOperationRequest).GetProperty(nameof(BatchOperationRequest.Operation));
        Assert.NotNull(property);
        var description = property!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);

        var allOperations = BatchOperationCatalog.ReadOperationNames
            .Concat(BatchOperationCatalog.WriteOperationNames);
        foreach (var operation in allOperations)
        {
            Assert.Contains(operation, description!.Description);
        }
    }

    [Fact]
    public void PreviewWriteBatchDescription_StatesTheActualTokenLifetime()
    {
        var expected = $"{WriteSafetyService.DefaultTokenLifetime.TotalMinutes:N0} minutes";
        Assert.Contains(expected, MethodDescription("PreviewWriteBatch"));
    }

    [Theory]
    [InlineData(nameof(BatchOperationRequest.OperationId))]
    [InlineData(nameof(BatchOperationRequest.Operation))]
    [InlineData(nameof(BatchOperationRequest.ProjectPath))]
    [InlineData(nameof(BatchOperationRequest.BlockPath))]
    [InlineData(nameof(BatchOperationRequest.YamlContent))]
    [InlineData(nameof(BatchOperationRequest.Query))]
    [InlineData(nameof(BatchOperationRequest.TableName))]
    [InlineData(nameof(BatchOperationRequest.Name))]
    [InlineData(nameof(BatchOperationRequest.DataType))]
    [InlineData(nameof(BatchOperationRequest.Value))]
    [InlineData(nameof(BatchOperationRequest.TypeIdentifier))]
    [InlineData(nameof(BatchOperationRequest.DeviceName))]
    public void KeyRequestFieldsHaveDescriptions(string propertyName)
    {
        var property = typeof(BatchOperationRequest).GetProperty(propertyName);
        Assert.NotNull(property);
        var description = property!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        Assert.False(string.IsNullOrWhiteSpace(description!.Description));
    }
}
