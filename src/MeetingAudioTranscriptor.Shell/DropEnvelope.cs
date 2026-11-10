// The drop message a host hands the bridge when files are dropped on the window, and the decoding of
// the file URIs the Linux toolkit delivers a drop as.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>Builds <c>{"drop":{"paths":[...]}}</c> and turns <c>file://</c> URIs into local paths.</summary>
internal static class DropEnvelope
{
    /// <summary>The envelope for a set of dropped paths, in the order they were dropped.</summary>
    public static string FromPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("drop");
            writer.WriteStartArray("paths");
            foreach (string path in paths)
            {
                writer.WriteStringValue(path);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The local paths of the <c>file://</c> URIs among the given ones; a URI of another scheme, a
    /// remote host or a broken escape is skipped, because it names nothing on this machine.
    /// </summary>
    public static IReadOnlyList<string> PathsFromUris(IEnumerable<string> uris)
    {
        ArgumentNullException.ThrowIfNull(uris);

        List<string> paths = [];
        foreach (string uri in uris)
        {
            string? path = LocalPathOf(uri.Trim());
            if (path is not null)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// The path of one <c>file://</c> URI: the authority must be empty or "localhost", and the path's
    /// percent escapes are decoded as UTF-8 bytes, which is how a file manager writes a non-ASCII name.
    /// </summary>
    public static string? LocalPathOf(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        const string Scheme = "file://";
        if (!uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = uri[Scheme.Length..];
        int slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            return null;
        }

        string authority = rest[..slash];
        if (authority.Length > 0 && !authority.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return DecodePercentUtf8(rest[slash..]);
    }

    private static string? DecodePercentUtf8(string encoded)
    {
        List<byte> bytes = new(encoded.Length);
        for (int i = 0; i < encoded.Length; i++)
        {
            char character = encoded[i];
            if (character == '%')
            {
                if (i + 2 >= encoded.Length || !IsHex(encoded[i + 1]) || !IsHex(encoded[i + 2]))
                {
                    return null;
                }

                bytes.Add((byte)((HexValue(encoded[i + 1]) << 4) | HexValue(encoded[i + 2])));
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(character.ToString()));
            }
        }

        string path = Encoding.UTF8.GetString(bytes.ToArray());
        return path.Contains('\0', StringComparison.Ordinal) ? null : path;
    }

    private static bool IsHex(char character) => char.IsAsciiHexDigit(character);

    private static int HexValue(char character) =>
        character <= '9' ? character - '0' : (char.ToLowerInvariant(character) - 'a') + 10;
}
