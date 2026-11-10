// The embedded page: the frontend tree compiled into the assembly, looked up by the path a URL
// asks for, with a 404 for every name the page does not hold.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>One answer to an asset request: a status, a content type and the body.</summary>
/// <param name="StatusCode">200 for an embedded file, 404 for everything else.</param>
/// <param name="ReasonPhrase">The status line's phrase.</param>
/// <param name="ContentType">The media type the body is served as.</param>
/// <param name="Body">The file's bytes, or a short sentence for a 404.</param>
internal sealed record AssetResponse(int StatusCode, string ReasonPhrase, string ContentType, byte[] Body)
{
    /// <summary>The headers this response carries, shared by both hosts.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers => ResponseHeaders.For(ContentType);

    /// <summary>The content type without its parameters, such as "text/css" for "text/css; charset=utf-8".</summary>
    public string MediaType
    {
        get
        {
            int parameters = ContentType.IndexOf(';', StringComparison.Ordinal);
            return parameters < 0 ? ContentType : ContentType[..parameters].Trim();
        }
    }
}

/// <summary>
/// Serves the page compiled into the assembly. Both hosts call it with what follows their origin,
/// so the same request gets the same bytes on Windows and on Linux.
/// </summary>
/// <remarks>
/// Every page file is embedded under a logical name starting "frontend/", and a lookup only ever
/// reads names under that prefix, so no other resource of the assembly can be served. A path is
/// refused before the lookup when it holds a dot segment, an empty segment, a backslash or a control
/// character, so a traversal is impossible by construction rather than by string cleaning.
/// </remarks>
internal static class EmbeddedAssets
{
    /// <summary>The origin the Windows host serves the page from: a virtual HTTPS host, never resolved.</summary>
    public const string WindowsOrigin = "https://meeting-audio-transcriptor.app/";

    /// <summary>The origin the Linux host serves the page from: a custom scheme the web context answers.</summary>
    public const string LinuxOrigin = "app://meeting-audio-transcriptor/";

    /// <summary>The resource-name prefix of every page file.</summary>
    private const string ResourcePrefix = "frontend/";

    /// <summary>
    /// The media types the page is made of. A file kind outside the table is served as octets, which
    /// a browser refuses to run as a module or a style sheet, so a new kind is added here on purpose.
    /// </summary>
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".json"] = "application/json; charset=utf-8",
        [".woff2"] = "font/woff2",
    };

    private static readonly Assembly Shell = typeof(EmbeddedAssets).Assembly;

    /// <summary>
    /// The answer for an absolute URL: the embedded file when the URL is on the origin and names one,
    /// and a 404 otherwise, including for every URL off the origin.
    /// </summary>
    public static AssetResponse ForUri(string uri, string origin)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(origin);

        if (!uri.StartsWith(origin, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        return ForPath(uri[origin.Length..]);
    }

    /// <summary>
    /// The answer for the part of a URL after the origin, with or without its leading slash; a query
    /// and a fragment are ignored, and the empty path is the page itself.
    /// </summary>
    public static AssetResponse ForPath(string urlPath)
    {
        ArgumentNullException.ThrowIfNull(urlPath);

        string? name = ResourceNameFor(urlPath);
        if (name is null)
        {
            return NotFound();
        }

        using Stream? stream = Shell.GetManifestResourceStream(ResourcePrefix + name);
        if (stream is null)
        {
            return NotFound();
        }

        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return new AssetResponse(200, "OK", MediaTypeOf(name), copy.ToArray());
    }

    /// <summary>
    /// The page-relative file name a URL path asks for, or null when the path may not name a file:
    /// it is percent-decoded once and then refused if any segment is empty, "." or "..", or if it
    /// holds a backslash or a control character.
    /// </summary>
    public static string? ResourceNameFor(string urlPath)
    {
        ArgumentNullException.ThrowIfNull(urlPath);

        int cut = urlPath.IndexOfAny(['?', '#']);
        string path = cut >= 0 ? urlPath[..cut] : urlPath;

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (decoded.StartsWith('/'))
        {
            decoded = decoded[1..];
        }

        if (decoded.Length == 0)
        {
            return "index.html";
        }

        foreach (char character in decoded)
        {
            if (character == '\\' || char.IsControl(character))
            {
                return null;
            }
        }

        foreach (string segment in decoded.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return null;
            }
        }

        return decoded;
    }

    /// <summary>The media type a file is served as, by its extension.</summary>
    public static string MediaTypeOf(string name) =>
        MediaTypes.TryGetValue(Path.GetExtension(name), out string? known) ? known : "application/octet-stream";

    /// <summary>The page-relative names of every embedded page file, for tests and diagnostics.</summary>
    public static IReadOnlyList<string> Names()
    {
        List<string> names = [];
        foreach (string resource in Shell.GetManifestResourceNames())
        {
            if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                names.Add(resource[ResourcePrefix.Length..]);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static AssetResponse NotFound() =>
        new(404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found"));
}
