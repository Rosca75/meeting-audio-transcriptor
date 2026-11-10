// The headers every embedded asset response carries: a strict content security policy, no caching
// and no content sniffing, the same on both hosts.
using System.Collections.Generic;
using System.Text;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>The response headers of the embedded page, shared by both hosts.</summary>
internal static class ResponseHeaders
{
    /// <summary>
    /// The page may load only its own scripts, styles and images (and data images), and may open no
    /// connection, embed no plug-in, change no base address and submit no form. A markup escape a
    /// template misses therefore has nowhere to send anything.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>No caching, so a rebuilt executable never serves yesterday's page from the web view's cache.</summary>
    public const string CacheControl = "no-store";

    /// <summary>The declared content type is the only one the web view may use.</summary>
    public const string ContentTypeOptions = "nosniff";

    /// <summary>The headers of one response with the given content type, in a fixed order.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> For(string contentType) =>
    [
        new("Content-Type", contentType),
        new("Content-Security-Policy", ContentSecurityPolicy),
        new("Cache-Control", CacheControl),
        new("X-Content-Type-Options", ContentTypeOptions),
    ];

    /// <summary>
    /// The same headers as one raw block, one "Name: value" per line, which is the form the Windows
    /// web view takes when a response is created.
    /// </summary>
    public static string AsBlock(string contentType)
    {
        StringBuilder block = new();
        foreach (KeyValuePair<string, string> header in For(contentType))
        {
            if (block.Length > 0)
            {
                block.Append('\n');
            }

            block.Append(header.Key).Append(": ").Append(header.Value);
        }

        return block.ToString();
    }
}
