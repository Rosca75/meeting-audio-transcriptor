// Guard: every component a package ships is credited in NOTICE, so a model or binary cannot be added to the locks without its licence and attribution.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed class NoticeGuardTests
{
    [Fact]
    public void EveryDefaultModelOfTheLockIsNamedInNoticeWithItsLicence()
    {
        string notice = ReadNotice();
        using JsonDocument models = ReadLock("build/models.lock");
        List<JsonElement> shipped = models.RootElement.GetProperty("models").EnumerateArray()
            .Where(model => model.GetProperty("set").GetString() == "default")
            .ToList();

        Assert.True(shipped.Count > 0, "build/models.lock lists no model of the default set, wanted at least one; the guard cannot run");
        List<string> missing = [];
        foreach (JsonElement model in shipped)
        {
            string name = model.GetProperty("name").GetString()!;
            if (!notice.Contains(name, StringComparison.Ordinal))
            {
                missing.Add($"the model '{name}' is not named");
            }

            foreach (string id in LicenceIds(model.GetProperty("licence").GetString()!))
            {
                if (!notice.Contains(id, StringComparison.Ordinal))
                {
                    missing.Add($"the licence '{id}' of the model '{name}' is not named");
                }
            }
        }

        Assert.True(missing.Count == 0, $"NOTICE does not credit what build/models.lock ships: {string.Join("; ", missing)}. Add a section with the component's name, version, licence, copyright and source to NOTICE");
    }

    [Fact]
    public void NoCandidateModelOfTheLockIsNamedInNotice()
    {
        string notice = ReadNotice();
        using JsonDocument models = ReadLock("build/models.lock");
        List<string> candidates = models.RootElement.GetProperty("models").EnumerateArray()
            .Where(model => model.GetProperty("set").GetString() == "candidate")
            .Select(model => model.GetProperty("name").GetString()!)
            .ToList();

        Assert.True(candidates.Count > 0, "build/models.lock lists no candidate model, wanted at least one; the guard cannot run");
        List<string> listed = candidates.Where(name => notice.Contains(name, StringComparison.Ordinal)).ToList();

        Assert.True(listed.Count == 0, $"NOTICE names models that no package carries: {string.Join(", ", listed)}. NOTICE lists what ships, so remove their sections, or move the model to the default set of build/models.lock");
    }

    [Fact]
    public void EveryFfmpegBuildOfTheLockIsNamedInNoticeWithItsVersionAndLicence()
    {
        string notice = ReadNotice();
        using JsonDocument ffmpeg = ReadLock("build/ffmpeg.lock");
        List<JsonElement> builds = ffmpeg.RootElement.GetProperty("builds").EnumerateArray().ToList();

        Assert.True(builds.Count > 0, "build/ffmpeg.lock lists no build, wanted at least one; the guard cannot run");
        Assert.Contains("ffmpeg", notice, StringComparison.Ordinal);
        List<string> missing = [];
        foreach (JsonElement build in builds)
        {
            string version = build.GetProperty("version").GetString()!;
            if (!notice.Contains(version, StringComparison.Ordinal))
            {
                missing.Add($"the version '{version}' of the {build.GetProperty("rid").GetString()} build is not named");
            }

            string licence = build.GetProperty("licence").GetString()!;
            if (!notice.Contains(licence, StringComparison.Ordinal))
            {
                missing.Add($"the licence '{licence}' of the {build.GetProperty("rid").GetString()} build is not named");
            }
        }

        Assert.True(missing.Count == 0, $"NOTICE does not credit what build/ffmpeg.lock ships: {string.Join("; ", missing)}. Update the ffmpeg section of NOTICE to the pinned build");
    }

    /// <summary>The licence identifiers in a lock's free-text licence field, such as MIT, Apache-2.0 and CC-BY-4.0.</summary>
    private static IEnumerable<string> LicenceIds(string licence)
    {
        // An identifier is MIT or capitalised words joined by hyphens and ending in a version.
        return Regex.Matches(licence, @"\bMIT\b|\b[A-Z][A-Za-z]*(?:-[0-9A-Za-z.]+)+\b")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal);
    }

    private static string ReadNotice()
    {
        string path = Path.Combine(RepositoryFiles.Root, "NOTICE");
        Assert.True(File.Exists(path), "NOTICE is missing from the repository root, wanted the credits every package carries; restore it");
        return File.ReadAllText(path);
    }

    private static JsonDocument ReadLock(string relativePath)
    {
        string path = Path.Combine(RepositoryFiles.Root, relativePath);
        Assert.True(File.Exists(path), $"{relativePath} is missing, wanted the lock the build ships from; restore it");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
