// Guard: every source, project and script file of the repository opens with a purpose header comment.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed class PurposeHeaderGuardTests
{
    [Fact]
    public void EveryCSharpFileUnderSrcAndTestsOpensWithAComment()
    {
        List<string> files = RepositoryFiles.Under("src", "*.cs").Concat(RepositoryFiles.Under("tests", "*.cs")).ToList();

        Assert.True(files.Count > 0, "got no .cs file under src or tests, wanted at least one; the guard cannot run");
        AssertAllOpenWith(files, "//");
    }

    [Fact]
    public void EveryProjectFileOpensWithAnXmlComment()
    {
        IReadOnlyList<string> files = RepositoryFiles.Under("", "*.csproj");

        Assert.True(files.Count > 0, "got no .csproj file, wanted at least one; the guard cannot run");
        AssertAllOpenWith(files, "<!--");
    }

    [Fact]
    public void EveryScriptAndWorkflowFileOpensWithAComment()
    {
        // A file kind that does not exist yet is not an error: the rule applies as soon as one is added.
        // A shebang may precede the comment of a shell script, and a script may open with its own block.
        AssertAllOpenWith(RepositoryFiles.Under("", "*.js", "*.mjs"), "//");
        AssertAllOpenWith(RepositoryFiles.Under("", "*.yml", "*.yaml", "*.ps1", "*.sh"), "#");
    }

    private static void AssertAllOpenWith(IReadOnlyList<string> files, string marker)
    {
        List<string> offenders = [];
        foreach (string file in files)
        {
            string? first = File.ReadLines(file)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("#!", System.StringComparison.Ordinal));
            if (first is null || !first.StartsWith(marker, System.StringComparison.Ordinal))
            {
                offenders.Add(RepositoryFiles.Relative(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"got {offenders.Count} file(s) without a leading '{marker}' purpose header ({string.Join(", ", offenders)}), wanted every file to open with one; add a comment saying what the file is for");
    }
}
