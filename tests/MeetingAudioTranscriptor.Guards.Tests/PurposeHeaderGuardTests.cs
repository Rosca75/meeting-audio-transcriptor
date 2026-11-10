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
        // A shebang may precede the comment of a shell script, and a script may open with its own block.
        // Each group must find files (the build tools, the workflows and the publish script exist), so a
        // broken file listing fails here rather than passing over nothing.
        IReadOnlyList<string> scripts = RepositoryFiles.Under("", "*.js", "*.mjs");
        IReadOnlyList<string> hashed = RepositoryFiles.Under("", "*.yml", "*.yaml", "*.ps1", "*.sh");
        Assert.True(scripts.Count > 0, "got no .js or .mjs file, wanted the build tools and the page; the guard cannot run");
        Assert.True(hashed.Count > 0, "got no .yml, .ps1 or .sh file, wanted the workflows and the publish script; the guard cannot run");
        AssertAllOpenWith(scripts, "//");
        AssertAllOpenWith(hashed, "#");
    }

    [Fact]
    public void EveryFrontendStylesheetOpensWithABlockComment()
    {
        // HTML is exempt: a document type declaration must come first. The page's scripts are covered
        // by the script rule above.
        IReadOnlyList<string> files = RepositoryFiles.Under("frontend", "*.css");

        Assert.True(files.Count > 0, "got no .css file under frontend, wanted the page's stylesheets; the guard cannot run");
        AssertAllOpenWith(files, "/*");
    }

    [Fact]
    public void EveryFrontendScriptIsCoveredByTheScriptRule()
    {
        IReadOnlyList<string> files = RepositoryFiles.Under("frontend", "*.js");

        Assert.True(files.Count > 0, "got no .js file under frontend, wanted the page's modules; the guard cannot run");
        AssertAllOpenWith(files, "//");
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
