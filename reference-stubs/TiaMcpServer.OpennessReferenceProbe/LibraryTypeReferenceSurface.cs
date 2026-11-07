using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only V21 signatures; never executed by the verifier.
    internal static class LibraryTypeReferenceSurface
    {
        private static void Types(LibraryTypeComposition types, LibraryType type,
            LibraryTypeVersion version, LibraryTypeInstanceInfo instance, ILibrary library,
            PlcSoftware plc, PlcBlockComposition blocks, PlcTypeComposition plcTypes,
            CodeBlockLibraryTypeVersion codeVersion, PlcTypeLibraryTypeVersion plcVersion, PlcType plcType)
        {
            Func<DirectoryInfo, string, IEngineeringObject, LibraryImportOptions, TypeCreateTransferResults> create = types.CreateFromDocuments;
            Func<DirectoryInfo, string, CreateOptions, LibraryImportOptions, VersionCreateTransferResults> createVersion = type.Versions.CreateFromDocuments;
            TypeCreateTransferResults created = create(null, null, null, LibraryImportOptions.None);
            LibraryType createdType = created.CreatedType;
            TransferResultState state = created.TransferResultState;
            VersionCreateTransferResults createdVersion = createVersion(null, null, CreateOptions.None, LibraryImportOptions.None);
            LibraryTypeVersion newVersion = createdVersion.CreatedVersion;
            TransferResultState versionState = createdVersion.TransferResultState;
            createVersion(null, null, CreateOptions.Override, LibraryImportOptions.None);
            Action<CreateOrReleaseDependenciesMode, Version, string, string> release = version.Release;
            release(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, null, null, null);
            Action setDefault = version.SetAsDefault;
            Action discard = version.Discard;
            Action deleteVersion = version.Delete;
            IInstanceSearchScope scope = plc;
            Func<IInstanceSearchScope, IList<LibraryTypeInstanceInfo>> findInstances = version.FindInstances;
            LibraryTypeVersion linked = instance.LibraryTypeVersion;
            IEngineeringObject linkedObject = instance.LibraryTypeInstance;
            Version number = version.VersionNumber;
            LibraryTypeVersionState versionStatus = version.State;
            bool isDefault = version.IsDefault;
            Guid guid = version.Guid;
            LibraryType owner = version.TypeObject;
            Action deleteType = type.Delete;
            Action<ILibrary, DeleteUnusedVersionsMode, StructureConflictResolutionMode, ForceUpdateMode> update = type.UpdateLibrary;
            update(library, DeleteUnusedVersionsMode.DoNotDelete, StructureConflictResolutionMode.RetainStructure, ForceUpdateMode.NoDefaultVersionChange);
            Func<CodeBlockLibraryTypeVersion, UpdatePathsMode, PlcBlock> instantiateBlock = blocks.CreateFrom;
            Func<PlcTypeLibraryTypeVersion, UpdatePathsMode, PlcType> instantiateType = plcTypes.CreateFrom;
            instantiateBlock(codeVersion, UpdatePathsMode.ThrowIfPathsConflict);
            instantiateType(plcVersion, UpdatePathsMode.ThrowIfPathsConflict);
            Action deletePlcType = plcType.Delete;
        }
    }
}
