using System.Text;

namespace TiaMcpServer.OperationBatches;

public static class OperationBatchStateComposer
{
    private const string StateSeparator = "\n--- batch item ---\n";

    public static string CombineCurrentState(IReadOnlyList<OperationBatchCurrentState> states)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < states.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(StateSeparator);
            }

            var state = states[i];
            builder.Append(state.OperationId).Append("::").Append(state.Operation).Append('\n').Append(state.CurrentState);
        }

        return builder.ToString();
    }

    public static string? ResolveProjectPath<T>(IReadOnlyList<T> operations)
        where T : IOperationBatchItem
        => operations.FirstOrDefault(operation => !string.IsNullOrWhiteSpace(operation.ProjectPath))?.ProjectPath;
}
