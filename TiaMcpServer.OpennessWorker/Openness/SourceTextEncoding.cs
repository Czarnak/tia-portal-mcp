using System;
using System.Text;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// BOM and line-ending handling for Siemens external-source text (.udt, .db, .scl).
///
/// <para>
/// TIA Portal writes these files as UTF-8 WITH a byte order mark and CRLF line endings. The BOM is
/// meaningful on disk and noise inside a JSON string payload, so it is stripped on the way out and
/// restored on the way in. Line endings are normalized to CRLF on the way in because a client that
/// edits the text through a JSON round trip may well hand back bare LF.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class SourceTextEncoding
{
    private const char ByteOrderMark = '\uFEFF';

    // GetBytes never emits the preamble regardless of encoderShouldEmitUTF8Identifier -- only
    // GetPreamble does, which is why the BOM is concatenated explicitly below.
    private static readonly byte[] Utf8Preamble =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();

    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Disk text to payload text: drop the BOM, leave everything else alone.</summary>
    public static string ForTransport(string fileText)
    {
        if (string.IsNullOrEmpty(fileText))
        {
            return string.Empty;
        }

        return fileText[0] == ByteOrderMark ? fileText.Substring(1) : fileText;
    }

    /// <summary>Payload text to disk bytes: normalize to CRLF and prepend the BOM.</summary>
    public static byte[] ForFile(string transportText)
    {
        var text = transportText ?? string.Empty;

        if (text.Length > 0 && text[0] == ByteOrderMark)
        {
            text = text.Substring(1);
        }

        var body = Utf8NoBom.GetBytes(NormalizeToCrLf(text));
        var bytes = new byte[Utf8Preamble.Length + body.Length];
        Buffer.BlockCopy(Utf8Preamble, 0, bytes, 0, Utf8Preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, Utf8Preamble.Length, body.Length);
        return bytes;
    }

    private static string NormalizeToCrLf(string text)
    {
        var builder = new StringBuilder(text.Length + 16);

        for (int i = 0; i < text.Length; i++)
        {
            var character = text[i];

            if (character == '\r')
            {
                builder.Append("\r\n");
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            if (character == '\n')
            {
                builder.Append("\r\n");
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}