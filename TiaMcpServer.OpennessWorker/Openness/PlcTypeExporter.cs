using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.SW.ExternalSources;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Exports one PlcType as either Siemens external-source text (.udt) or Simatic ML.
///
/// <para>
/// Returns raw text with no bundle envelope: unlike a block export, which carries an .xml plus a
/// companion .s7dcl/.s7res pair and therefore needs BlockBundleFormat's delimiters, a type export
/// is a single document with nothing to delimit.
/// </para>
/// </summary>
internal static class PlcTypeExporter
{
    public static string Export(Project project, string typePath, string format)
    {
        var address = PlcTypeAddress.Parse(typePath);
        var target = PlcTypeTargetResolver.ResolveForExport(project, address);

        if (target.Type is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No PLC data type was found at '{address.ToDisplayPath()}'.");
        }

        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "tia-mcp-type-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            return string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal)
                ? ExportXml(target, tempDirectory)
                : ExportSource(project, target, tempDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static string ExportSource(Project project, ResolvedTypeTarget target, string tempDirectory)
    {
        var path = Path.Combine(tempDirectory, target.DocumentName + ".udt");
        var plcSoftware = PlcSoftwareLocator.ForType(project, target.Type!);

        plcSoftware.ExternalSourceGroup.GenerateSource(
            new List<IGenerateSource> { target.Type! },
            new FileInfo(path));

        if (!File.Exists(path))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal reported no error but produced no source file for "
                + $"'{target.DocumentName}'. Compile the type in TIA Portal and try again.");
        }

        return SourceTextEncoding.ForTransport(File.ReadAllText(path));
    }

    private static string ExportXml(ResolvedTypeTarget target, string tempDirectory)
    {
        var path = Path.Combine(tempDirectory, target.DocumentName + ".xml");
        target.Type!.Export(new FileInfo(path), ExportOptions.None);

        return BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(path));
    }
}
