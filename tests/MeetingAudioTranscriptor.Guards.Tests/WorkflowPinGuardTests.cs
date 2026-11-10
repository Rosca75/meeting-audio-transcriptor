// Guard: every action a workflow or composite action uses is pinned by a full commit SHA, because a version tag can be moved to code nobody reviewed.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed partial class WorkflowPinGuardTests
{
    /// <summary>A "uses:" line: the key, then the reference up to a space, a comment or the end of the line.</summary>
    [GeneratedRegex(@"^\s*(?:-\s+)?uses:\s*['""]?(?<ref>[^\s'""#]+)", RegexOptions.Multiline)]
    private static partial Regex UsesLine();

    /// <summary>A pin: owner/repo, an optional path inside it, then "@" and 40 hexadecimal characters.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(/[A-Za-z0-9_./-]+)?@[0-9a-f]{40}$")]
    private static partial Regex FullShaPin();

    [Fact]
    public void EveryUsesReferenceIsAFullCommitShaOrALocalAction()
    {
        List<string> files = RepositoryFiles.Under(".github", "*.yml", "*.yaml").ToList();

        Assert.True(files.Count > 0, "got no workflow or action file under .github, wanted at least one; the guard cannot run");
        int references = 0;
        List<string> unpinned = [];
        foreach (string file in files)
        {
            foreach (Match match in UsesLine().Matches(File.ReadAllText(file)))
            {
                references++;
                string reference = match.Groups["ref"].Value;

                // A local composite action lives in this repository and is reviewed with it.
                if (reference.StartsWith("./", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!FullShaPin().IsMatch(reference))
                {
                    unpinned.Add($"{RepositoryFiles.Relative(file)}: {reference}");
                }
            }
        }

        Assert.True(references > 0, "found no 'uses:' line in any workflow, wanted at least one; the guard's pattern may be wrong");
        Assert.True(
            unpinned.Count == 0,
            $"these actions are not pinned by a full 40-character commit SHA: {string.Join("; ", unpinned)}. Look up the commit of the release tag and write it as owner/repo@<sha> # vX.Y.Z");
    }
}
