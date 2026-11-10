// Guard: no em dash (U+2014) in the page's copy or in any C# source under src/, because every
// user-visible sentence is English prose without one.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed class NoEmDashInCopyGuardTests
{
    private const char EmDash = '—';

    private static List<string> WithEmDash(IEnumerable<string> files)
    {
        return files.Where(file => File.ReadAllText(file).Contains(EmDash)).Select(RepositoryFiles.Relative).ToList();
    }

    [Fact]
    public void CopyJsHasNoEmDash()
    {
        string copy = Path.Combine(RepositoryFiles.Root, "frontend", "js", "copy.js");
        Assert.True(File.Exists(copy), "got no frontend/js/copy.js, wanted the page's copy file; the guard cannot run");

        Assert.True(WithEmDash([copy]).Count == 0, "got an em dash in frontend/js/copy.js, wanted commas, periods or parentheses instead");
    }

    [Fact]
    public void NoCSharpSourceUnderSrcHasAnEmDash()
    {
        IReadOnlyList<string> files = RepositoryFiles.Under("src", "*.cs");
        Assert.True(files.Count > 0, "got no .cs file under src, wanted at least one; the guard cannot run");

        List<string> offenders = WithEmDash(files);

        Assert.True(offenders.Count == 0, $"got an em dash in {string.Join(", ", offenders)}, wanted none; use commas, periods or parentheses");
    }
}
