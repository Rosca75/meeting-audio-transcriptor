// Guard: the page suite exists and runs, because a test file the command never reaches, or one that
// is silenced, proves nothing about the page.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed partial class PageTestsPresentTests
{
    private const string Command = "node --test \"frontend/**/*.test.js\"";

    [GeneratedRegex(@"\b(?:test|it|describe)\.(?:skip|only|todo)\b|\{\s*(?:skip|todo)\s*:")]
    private static partial Regex SilencedTest();

    [GeneratedRegex(@"^\s*(?:test|it)\(", RegexOptions.Multiline)]
    private static partial Regex TestCall();

    private static IReadOnlyList<string> TestFiles()
    {
        IReadOnlyList<string> files = RepositoryFiles.Under("frontend", "*.test.js");
        Assert.True(files.Count > 0, $"got no *.test.js file under frontend/, wanted the page suite that `{Command}` runs");
        return files;
    }

    [Fact]
    public void EveryPageModuleWithLogicHasATestFileBesideIt()
    {
        string[] logic = ["api", "bridge", "controller", "model", "state", "copy"];
        HashSet<string> present = TestFiles().Select(f => Path.GetFileName(f).Replace(".test.js", "", System.StringComparison.Ordinal)).ToHashSet();

        List<string> missing = logic.Where(name => !present.Contains(name)).ToList();

        Assert.True(missing.Count == 0, $"got no test file for the modules ({string.Join(", ", missing)}), wanted frontend/js/<module>.test.js for each");
    }

    [Fact]
    public void EveryTestFileHoldsRealUnsilencedTests()
    {
        List<string> offenders = [];
        foreach (string file in TestFiles())
        {
            string text = File.ReadAllText(file);
            if (!TestCall().IsMatch(text) || SilencedTest().IsMatch(text))
            {
                offenders.Add(RepositoryFiles.Relative(file));
            }
        }

        Assert.True(offenders.Count == 0, $"got test files with no test or with a skipped, only or todo test ({string.Join(", ", offenders)}), wanted every file to run all its tests");
    }

    [Fact]
    public void TheChartersDocumentTheCommandThatRunsThePageSuite()
    {
        foreach (string document in new[] { "frontend/CLAUDE.md", "docs/TESTING.md" })
        {
            string text = File.ReadAllText(Path.Combine(RepositoryFiles.Root, document));
            Assert.True(text.Contains(Command, System.StringComparison.Ordinal), $"got no `{Command}` in {document}, wanted the page suite's command documented there");
        }
    }
}
