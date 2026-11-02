using Siemens.Engineering;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

internal static partial class Program
{
    private static class PlcWorkerDispatch
    {
        public static WorkerResponse GetBlockContent(WorkerRequest request)
        {
            if (string.IsNullOrEmpty(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            var format = NormalizeFormat(request.Format, SourceFormatNames.Xml);
            var withDependencies = request.WithDependencies == true;

            return WithEngineeringRoot(request, project =>
            {
                var content = BlockExporter.Export(project, request.BlockPath!, format, withDependencies);
                return RawPayload(content, SourceReadWarnings.ForExport(withDependencies, format, content));
            });
        }

        public static WorkerResponse UpdateBlockLogic(WorkerRequest request)
        {
            var normalizedFormat = BlockUpdateOutcomeDecorator.NormalizeFormatOrNull(request.Format);
            var importerEntered = false;
            try
            {
                if (string.IsNullOrEmpty(request.BlockPath))
                {
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
                }

                if (string.IsNullOrEmpty(request.Content))
                {
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Content is required.");
                }

                normalizedFormat = NormalizeFormat(request.Format, SourceFormatNames.Xml);

                var response = WithEngineeringRoot(request, project =>
                {
                    importerEntered = true;
                    var result = BlockImporter.Import(project, request.BlockPath!, request.Content!, normalizedFormat, request.ExpectedContentHash);
                    return RawPayload(result.Payload, result.Warnings, result.Outcome);
                });
                return response.Success
                    ? response
                    : BlockUpdateOutcomeDecorator.Decorate(response, normalizedFormat, importerEntered);
            }
            catch (WorkerOperationException exception)
            {
                return BlockUpdateOutcomeDecorator.Decorate(exception, normalizedFormat, importerEntered);
            }
            catch (Exception exception)
            {
                return BlockUpdateOutcomeDecorator.Decorate(exception, normalizedFormat, importerEntered);
            }
        }

        public static WorkerResponse GetTypeContent(WorkerRequest request)
        {
            if (string.IsNullOrEmpty(request.TypePath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
            }

            var format = NormalizeFormat(request.Format, SourceFormatNames.Source);
            var withDependencies = request.WithDependencies == true;

            return WithEngineeringRoot(request, project =>
            {
                var content = PlcTypeExporter.Export(project, request.TypePath!, format, withDependencies);
                return RawPayload(content, SourceReadWarnings.ForExport(withDependencies, format, content));
            });
        }

        public static WorkerResponse UpdateTypeContent(WorkerRequest request)
        {
            if (string.IsNullOrEmpty(request.TypePath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
            }

            if (string.IsNullOrEmpty(request.Content))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Content is required.");
            }

            var format = NormalizeFormat(request.Format, SourceFormatNames.Source);

            return WithEngineeringRoot(request, project => Success(
                PlcTypeImporter.Import(project, request.TypePath!, request.Content!, format, request.ExpectedContentHash)));
        }

        // Direct worker callers retain each operation's default: XML for blocks, source for types.
        private static string NormalizeFormat(string? format, string fallback)
        {
            if (!SourceFormatNames.TryNormalize(format, fallback, out var normalized, out var error))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    error ?? "Invalid format.");
            }

            return normalized;
        }

        public static WorkerResponse ListTagTables(WorkerRequest request)
        {
            return WithEngineeringRoot(request, project => Success(TagTableReader.ReadInventory(
                project, request.PlcName, request.TableName, request.FolderPath)));
        }

        public static WorkerResponse CompileCheck(WorkerRequest request)
        {
            return WithEngineeringRoot(request, project => Success(CompileChecker.Compile(project, request.PlcName, request.BlockPath)));
        }

        public static WorkerResponse CreateTagTable(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.CreateTagTable(project, request.PlcName, request.TableName!, request.FolderPath));
        }

        public static WorkerResponse DeleteTagTable(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.DeleteTagTable(project, request.PlcName, request.TableName!, request.FolderPath));
        }

        public static WorkerResponse CreateTag(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.CreateTag(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!,
                    request.DataType!,
                    request.LogicalAddress));
        }

        public static WorkerResponse UpdateTag(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.UpdateTag(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!,
                    request.NewName,
                    request.DataType,
                    request.LogicalAddress,
                    request.ExternalAccessible,
                    request.ExternalVisible,
                    request.ExternalWritable));
        }

        public static WorkerResponse DeleteTag(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.DeleteTag(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!));
        }

        public static WorkerResponse CreateUserConstant(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.CreateUserConstant(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!,
                    request.DataType!,
                    request.Value!));
        }

        public static WorkerResponse UpdateUserConstant(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.UpdateUserConstant(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!,
                    request.DataType,
                    request.Value));
        }

        public static WorkerResponse DeleteUserConstant(WorkerRequest request)
        {
            return TagMutation(request, project =>
                TagMutationService.DeleteUserConstant(
                    project,
                    request.PlcName,
                    request.TableName!,
                    request.FolderPath,
                    request.Name!));
        }

        public static WorkerResponse CreateBlock(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            if (string.IsNullOrWhiteSpace(request.BlockType))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    "BlockType is required. Valid values: FB, FC, OB, GlobalDB.");
            }

            return WithEngineeringRoot(request, project => Success(
                BlockMutationService.CreateBlock(
                    project,
                    request.BlockPath!,
                    request.BlockType!,
                    request.Language,
                    request.OBEventClass)));
        }

        public static WorkerResponse DeleteBlock(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            return WithEngineeringRoot(request, project => Success(
                BlockMutationService.DeleteBlock(project, request.BlockPath!)));
        }

        public static WorkerResponse CreateBlockGroup(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            return WithEngineeringRoot(request, project => Success(
                BlockMutationService.CreateBlockGroup(project, request.BlockPath!)));
        }

        public static WorkerResponse DeleteBlockGroup(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            return WithEngineeringRoot(request, project => Success(
                BlockMutationService.DeleteBlockGroup(project, request.BlockPath!)));
        }

        private static WorkerResponse TagMutation(WorkerRequest request, Func<ProjectBase, TagMutationResultInfo> mutate)
        {
            return WithEngineeringRoot(request, project => Success(mutate(project)));
        }
    }
}
