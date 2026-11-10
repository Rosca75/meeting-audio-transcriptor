// Tests that the embedded page is served by path with the right types, that everything else is a 404,
// and that no path can climb out of the page's own files.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class EmbeddedAssetsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("index.html")]
    [InlineData("/index.html")]
    [InlineData("/?first=1#top")]
    public void ThePageItselfIsIndexHtml(string path)
    {
        AssetResponse response = EmbeddedAssets.ForPath(path);

        Assert.True(response.StatusCode == 200, $"got {response.StatusCode} for '{path}', wanted 200");
        Assert.True(response.ContentType == "text/html; charset=utf-8", $"got '{response.ContentType}' for '{path}', wanted text/html");
        Assert.True(Encoding.UTF8.GetString(response.Body).Contains("<html", System.StringComparison.Ordinal), $"got a body without <html> for '{path}', wanted the page");
    }

    [Theory]
    [InlineData("/js/bridge.js", "text/javascript; charset=utf-8")]
    [InlineData("/css/style.css", "text/css; charset=utf-8")]
    public void PageFilesAreServedWithTheirType(string path, string wanted)
    {
        AssetResponse response = EmbeddedAssets.ForPath(path);

        Assert.True(response.StatusCode == 200, $"got {response.StatusCode} for '{path}', wanted 200");
        Assert.True(response.ContentType == wanted, $"got '{response.ContentType}' for '{path}', wanted '{wanted}'");
        Assert.True(response.Body.Length > 0, $"got an empty body for '{path}', wanted the file");
    }

    [Theory]
    [InlineData("a.html", "text/html; charset=utf-8")]
    [InlineData("a.js", "text/javascript; charset=utf-8")]
    [InlineData("a.css", "text/css; charset=utf-8")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.json", "application/json; charset=utf-8")]
    [InlineData("a.woff2", "font/woff2")]
    [InlineData("A.PNG", "image/png")]
    [InlineData("a.exe", "application/octet-stream")]
    [InlineData("noextension", "application/octet-stream")]
    public void EachKindHasItsMediaType(string name, string wanted)
    {
        string got = EmbeddedAssets.MediaTypeOf(name);

        Assert.True(got == wanted, $"got '{got}' for '{name}', wanted '{wanted}'");
    }

    [Theory]
    [InlineData("/missing.png")]
    [InlineData("/js/missing.js")]
    [InlineData("/../index.html")]
    [InlineData("/js/../index.html")]
    [InlineData("/%2e%2e/index.html")]
    [InlineData("/js%2f..%2findex.html")]
    [InlineData("/./index.html")]
    [InlineData("//index.html")]
    [InlineData("/js//bridge.js")]
    [InlineData("/js\\bridge.js")]
    [InlineData("/js%5cbridge.js")]
    [InlineData("/index.html%00")]
    [InlineData("/%E0%A4%A")]
    [InlineData("/frontend/index.html")]
    [InlineData("/BRIDGE.md")]
    [InlineData("/CLAUDE.md")]
    [InlineData("/package.json")]
    [InlineData("/frontend-hashes.txt")]
    [InlineData("/js/api.test.js")]
    [InlineData("/meeting-audio-transcriptor.dll")]
    public void AnythingElseIsNotFound(string path)
    {
        AssetResponse response = EmbeddedAssets.ForPath(path);

        Assert.True(response.StatusCode == 404, $"got {response.StatusCode} for '{path}', wanted 404");
    }

    [Theory]
    [InlineData("https://example.com/index.html")]
    [InlineData("https://meeting-audio-transcriptor.app.example.com/index.html")]
    [InlineData("http://meeting-audio-transcriptor.app/index.html")]
    [InlineData("app://meeting-audio-transcriptor/index.html")]
    public void AnAddressOffTheWindowsOriginIsNotFound(string uri)
    {
        AssetResponse response = EmbeddedAssets.ForUri(uri, EmbeddedAssets.WindowsOrigin);

        Assert.True(response.StatusCode == 404, $"got {response.StatusCode} for '{uri}', wanted 404");
    }

    [Theory]
    [InlineData("https://meeting-audio-transcriptor.app/", EmbeddedAssets.WindowsOrigin)]
    [InlineData("HTTPS://MEETING-AUDIO-TRANSCRIPTOR.APP/js/main.js", EmbeddedAssets.WindowsOrigin)]
    [InlineData("app://meeting-audio-transcriptor/", EmbeddedAssets.LinuxOrigin)]
    [InlineData("app://meeting-audio-transcriptor/css/tokens.css?v=1", EmbeddedAssets.LinuxOrigin)]
    public void AnAddressOnTheOriginIsServed(string uri, string origin)
    {
        AssetResponse response = EmbeddedAssets.ForUri(uri, origin);

        Assert.True(response.StatusCode == 200, $"got {response.StatusCode} for '{uri}', wanted 200");
    }

    [Fact]
    public void OnlyThePageItselfIsEmbedded()
    {
        IReadOnlyList<string> names = EmbeddedAssets.Names();

        Assert.True(names.Contains("index.html"), $"got ({string.Join(", ", names)}), wanted index.html among them");
        Assert.True(names.Contains("js/bridge.js"), $"got ({string.Join(", ", names)}), wanted js/bridge.js among them");
        foreach (string name in names)
        {
            bool page = name == "index.html" || name.StartsWith("css/", System.StringComparison.Ordinal) || name.StartsWith("js/", System.StringComparison.Ordinal);
            Assert.True(page, $"got '{name}' embedded, wanted only index.html, css/ and js/");
            Assert.True(!name.EndsWith(".test.js", System.StringComparison.Ordinal), $"got the test file '{name}' embedded, wanted no page test");
        }
    }

    [Fact]
    public void EveryResponseCarriesTheStrictHeaders()
    {
        AssetResponse found = EmbeddedAssets.ForPath("/");
        AssetResponse missing = EmbeddedAssets.ForPath("/missing");

        foreach (AssetResponse response in new[] { found, missing })
        {
            Dictionary<string, string> headers = [];
            foreach (KeyValuePair<string, string> header in response.Headers)
            {
                headers[header.Key] = header.Value;
            }

            Assert.True(headers.GetValueOrDefault("Content-Security-Policy") == ResponseHeaders.ContentSecurityPolicy, $"got CSP '{headers.GetValueOrDefault("Content-Security-Policy")}' on a {response.StatusCode}, wanted the strict policy");
            Assert.True(headers.GetValueOrDefault("Cache-Control") == "no-store", $"got Cache-Control '{headers.GetValueOrDefault("Cache-Control")}', wanted no-store");
            Assert.True(headers.GetValueOrDefault("X-Content-Type-Options") == "nosniff", $"got X-Content-Type-Options '{headers.GetValueOrDefault("X-Content-Type-Options")}', wanted nosniff");
            Assert.True(headers.GetValueOrDefault("Content-Type") == response.ContentType, $"got Content-Type '{headers.GetValueOrDefault("Content-Type")}', wanted '{response.ContentType}'");
        }
    }

    [Fact]
    public void TheMediaTypeDropsTheParameters()
    {
        AssetResponse response = EmbeddedAssets.ForPath("/css/style.css");

        Assert.True(response.MediaType == "text/css", $"got '{response.MediaType}', wanted text/css");
    }
}
