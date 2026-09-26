using System;
using System.Collections.Generic;
using System.Text.Json;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class CompileReportProjection
{
    private const int MaxDepth = 16;
    private const int MaxMessages = 200;
    private const int MaxFieldCharacters = 1024;
    private const int MaxTextCharacters = 32000;
    private const int MaxSerializedCharacters = 60000;
    private const string OmissionNote = "Some compiler diagnostics were omitted or shortened because a report limit was reached or a detail could not be read.";

    // Default escaping and retained nulls conservatively cover the worker's compact camel-case
    // payload (which omits nulls) and the host's presentation of this document.
    private static readonly JsonSerializerOptions ReportJson = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal sealed class Budget
    {
        internal int MessageCount;
        internal int TextCharacters;
        internal bool Exhausted => MessageCount >= MaxMessages || TextCharacters >= MaxTextCharacters;

        internal string TakeText(string text, Projection projection)
        {
            var length = Math.Min(text.Length, Math.Min(MaxFieldCharacters, MaxTextCharacters - TextCharacters));
            if (length < text.Length)
            {
                projection.WasTruncated = true;
                if (length > 0 && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
                    length--;
            }

            TextCharacters += length;
            return length == text.Length ? text : text.Substring(0, length);
        }
    }

    internal sealed class Projection
    {
        public List<CompileMessageInfo> Messages { get; } = new List<CompileMessageInfo>();
        public bool WasTruncated { get; set; }
    }

    public static Projection Flatten<TMessage>(
        IEnumerable<TMessage> roots,
        Func<TMessage, string> readDescription,
        Func<TMessage, string> readPath,
        Func<TMessage, string> readSeverity,
        Func<TMessage, IEnumerable<TMessage>> readChildren,
        Budget budget)
    {
        var projection = new Projection();
        Visit(roots, 1);
        return projection;

        // Depth is one-based. On exhaustion, look at at most one additional element to
        // distinguish exact-boundary completion (including an empty next PLC) from omission.
        bool Visit(IEnumerable<TMessage> messages, int depth)
        {
            try
            {
                foreach (var message in messages)
                {
                    if (depth > MaxDepth)
                    {
                        projection.WasTruncated = true;
                        return true;
                    }
                    if (budget.Exhausted)
                    {
                        projection.WasTruncated = true;
                        return false;
                    }

                    budget.MessageCount++;
                    projection.Messages.Add(new CompileMessageInfo
                    {
                        Description = budget.TakeText(ReadText(readDescription, message), projection),
                        Path = budget.TakeText(ReadText(readPath, message), projection),
                        Severity = ReadText(readSeverity, message, "Information")
                    });

                    IEnumerable<TMessage> children;
                    try
                    {
                        children = readChildren(message);
                    }
                    catch (Exception)
                    {
                        projection.WasTruncated = true;
                        continue;
                    }
                    if (!Visit(children, depth + 1))
                        return false;
                }
            }
            catch (Exception)
            {
                // Enumeration/disposal can also cross the Siemens remoting boundary.
                projection.WasTruncated = true;
            }

            return true;
        }

        string ReadText(Func<TMessage, string> read, TMessage message, string fallback = "")
        {
            try { return read(message) ?? fallback; }
            catch (Exception)
            {
                projection.WasTruncated = true;
                return fallback;
            }
        }
    }

    public static void NoteOmission(PlcCompileInfo plc)
    {
        if (!plc.DiagnosticNotes.Contains(OmissionNote))
            plc.DiagnosticNotes.Add(OmissionNote);
    }

    public static void BoundSerializedReport(CompileCheckReport report)
    {
        // Raw field limits alone do not bound JSON: one UTF-16 character can become six
        // serialized characters. Remove trailing rows deterministically, preserving totals,
        // identities, and parent-before-child order. Never cut serialized JSON text.
        var plcIndex = report.Plcs.Count - 1;
        while (JsonSerializer.Serialize(report, ReportJson).Length >= MaxSerializedCharacters)
        {
            while (plcIndex >= 0 && report.Plcs[plcIndex].Messages.Count == 0)
                plcIndex--;

            if (plcIndex < 0)
                throw new InvalidOperationException("Compile report metadata exceeds the response limit; select a single PLC or a shorter block path.");

            var plc = report.Plcs[plcIndex];
            plc.Messages.RemoveAt(plc.Messages.Count - 1);
            NoteOmission(plc);
        }
    }
}
