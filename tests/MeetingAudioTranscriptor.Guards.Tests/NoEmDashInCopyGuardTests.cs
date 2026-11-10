// Guard: no em dash (U+2014) in the page's copy, in any C# source under src/ or in the documentation, because every
// user-visible sentence is English prose without one and the documents are read as copy.
using System;
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

    /// <summary>
    /// Documents allowed to hold an em dash, each with its reason: working documents that quote the title of a
    /// repository or a paper which is written with one.
    /// </summary>
    private static readonly Dictionary<string, string> ExemptDocuments = new(StringComparer.Ordinal)
    {
        ["docs/build/reference-digest.md"] = "quotes the title of the reference repository, which contains an em dash",
        ["docs/build/research-models.md"] = "quotes titles of papers and model cards, which contain em dashes",
    };

    [Fact]
    public void NoDocumentHasAnEmDash()
    {
        List<string> files =
        [
            .. RepositoryFiles.Under("docs", "*.md", "*.json"),
            .. RepositoryFiles.Under("", "README.md", "CHANGELOG.md", "NOTICE", "CLAUDE.md", "BRIDGE.md"),
        ];
        files = files
            .Distinct(StringComparer.Ordinal)
            .Where(file => !ExemptDocuments.ContainsKey(RepositoryFiles.Relative(file)))
            .ToList();
        Assert.True(files.Count > 10, $"got {files.Count} documents, wanted the docs folder, the root files and every charter; the guard cannot run");
        Assert.True(files.Any(file => RepositoryFiles.Relative(file) == "docs/transcript.schema.json"), "got no docs/transcript.schema.json among the documents, wanted it covered");

        List<string> offenders = WithEmDash(files);

        Assert.True(offenders.Count == 0, $"got an em dash in {string.Join(", ", offenders)}, wanted none; use commas, periods or parentheses, or add a working document to the exemption table with its reason");
    }
}
