// Guard: a package version appears in Directory.Packages.props alone, never in a project file.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed partial class PackageVersionGuardTests
{
    /// <summary>Matches a PackageReference element carrying a Version attribute; a reference without one does not match.</summary>
    [GeneratedRegex(@"<PackageReference\b[^>]*\bVersion\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex VersionedReference();

    [Fact]
    public void NoProjectFileVersionsAPackageReference()
    {
        IReadOnlyList<string> projects = RepositoryFiles.Under("", "*.csproj");
        Assert.True(projects.Count > 0, "got no .csproj file, wanted at least one; the guard cannot run");

        string[] offenders = projects
            .Where(path => VersionedReference().IsMatch(File.ReadAllText(path)))
            .Select(RepositoryFiles.Relative)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"got a Version on a PackageReference in ({string.Join(", ", offenders)}), wanted none; move the version to Directory.Packages.props");
    }
}
