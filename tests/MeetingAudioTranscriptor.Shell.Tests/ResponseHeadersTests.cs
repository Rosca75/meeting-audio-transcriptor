// Tests that the response headers are the strict set both hosts send, in the forms each host takes.
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class ResponseHeadersTests
{
    [Fact]
    public void ThePolicyIsTheStrictOne()
    {
        const string Wanted =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
            "connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";

        Assert.True(ResponseHeaders.ContentSecurityPolicy == Wanted, $"got '{ResponseHeaders.ContentSecurityPolicy}', wanted '{Wanted}'");
    }

    [Fact]
    public void TheBlockHasOneHeaderPerLine()
    {
        string block = ResponseHeaders.AsBlock("text/css; charset=utf-8");
        string wanted =
            "Content-Type: text/css; charset=utf-8\n" +
            "Content-Security-Policy: " + ResponseHeaders.ContentSecurityPolicy + "\n" +
            "Cache-Control: no-store\n" +
            "X-Content-Type-Options: nosniff";

        Assert.True(block == wanted, $"got '{block}', wanted '{wanted}'");
    }
}
