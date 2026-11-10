// Guard: every file under frontend/ is listed in frontend/frontend-hashes.txt with its SHA-256, because
// a silent edit to the page is a silent change to the contract it implements.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed class FrontendHashesGuardTests
{
    private const string HashFileName = "frontend-hashes.txt";
    private const string Fix = "run `node build/update-frontend-hashes.mjs` and commit the result with the page change";

    private static Dictionary<string, string> ReadListed()
    {
        string path = Path.Combine(RepositoryFiles.Root, "frontend", HashFileName);
        Assert.True(File.Exists(path), $"got no frontend/{HashFileName}, wanted one; {Fix}");

        Dictionary<string, string> listed = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path).Where(l => l.Length > 0))
        {
            // The format is "<sha256> *<relative path>", the same as sha256sum writes in binary mode.
            int split = line.IndexOf(" *", StringComparison.Ordinal);
            Assert.True(split == 64, $"got the line '{line}', wanted '<sha256> *<path>'; {Fix}");
            Assert.True(listed.TryAdd(line[(split + 2)..], line[..split]), $"got a duplicate line for '{line[(split + 2)..]}', wanted one per file; {Fix}");
        }

        return listed;
    }

    private static Dictionary<string, string> ActualHashes()
    {
        string frontend = Path.Combine(RepositoryFiles.Root, "frontend");
        Dictionary<string, string> actual = new(StringComparer.Ordinal);
        foreach (string file in RepositoryFiles.Under("frontend", "*"))
        {
            string relative = Path.GetRelativePath(frontend, file).Replace('\\', '/');
            if (relative == HashFileName)
            {
                continue;
            }

            actual[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return actual;
    }

    [Fact]
    public void EveryFrontendFileIsListed()
    {
        Dictionary<string, string> actual = ActualHashes();
        Assert.True(actual.Count > 0, "got no file under frontend/, wanted the page; the guard cannot run");

        List<string> unlisted = actual.Keys.Except(ReadListed().Keys).ToList();

        Assert.True(unlisted.Count == 0, $"got files missing from {HashFileName} ({string.Join(", ", unlisted)}), wanted every file listed; {Fix}");
    }

    [Fact]
    public void EveryListedFileExists()
    {
        List<string> missing = ReadListed().Keys.Except(ActualHashes().Keys).ToList();

        Assert.True(missing.Count == 0, $"got listed files that do not exist ({string.Join(", ", missing)}), wanted none; {Fix}");
    }

    [Fact]
    public void EveryListedHashMatchesTheFile()
    {
        Dictionary<string, string> actual = ActualHashes();
        List<string> changed = ReadListed()
            .Where(pair => actual.TryGetValue(pair.Key, out string? hash) && hash != pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        Assert.True(changed.Count == 0, $"got files whose bytes differ from their listed hash ({string.Join(", ", changed)}), wanted none; {Fix}");
    }
}
