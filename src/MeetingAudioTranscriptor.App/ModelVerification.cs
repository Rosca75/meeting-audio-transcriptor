// Checks the installed model files against the size and SHA-256 the models lock pins for each.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace MeetingAudioTranscriptor.App;

/// <summary>Verifies required model files against <c>models.lock</c>.</summary>
/// <remarks>
/// The check is per file rather than per model, because a run needs only some files of a model the
/// lock lists, and a required file the lock does not pin is reported as unverifiable, not as wrong.
/// </remarks>
internal static class ModelVerification
{
    private const string LockName = "models.lock";

    /// <summary>The lock beside the models, beside the executable, or in the repository the executable was built in.</summary>
    /// <exception cref="FileNotFoundException">None of them exists.</exception>
    public static string FindLock(string modelsRoot)
    {
        List<string> candidates = [Path.Combine(modelsRoot, LockName), Path.Combine(AppContext.BaseDirectory, LockName)];
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            candidates.Add(Path.Combine(folder.FullName, "build", LockName));
        }

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"no {LockName} was found beside the models ({modelsRoot}), beside the executable or in a build folder above it; pass the path of build/{LockName} from the repository");
    }

    /// <summary>One sentence per file that is missing, unpinned, of the wrong size or of the wrong hash.</summary>
    public static IReadOnlyList<string> Verify(string modelsRoot, IReadOnlyList<string> requiredFiles, string lockPath)
    {
        Dictionary<string, (string Sha256, long Bytes)> pinned = ReadLock(lockPath);
        List<string> problems = [];
        foreach (string file in requiredFiles)
        {
            string path = Path.Combine(modelsRoot, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                problems.Add($"{file} is missing from {modelsRoot}; fetch it with node build/fetch-models.mjs --set all --into {modelsRoot}");
                continue;
            }

            if (!pinned.TryGetValue(file, out (string Sha256, long Bytes) pin))
            {
                problems.Add($"{file} is not pinned in {lockPath}, so its hash cannot be checked; use a lock from the same revision as the application");
                continue;
            }

            long length = new FileInfo(path).Length;
            if (length != pin.Bytes)
            {
                problems.Add($"{file} has {length} bytes, wanted {pin.Bytes}; delete it and fetch it again");
                continue;
            }

            using FileStream stream = File.OpenRead(path);
            string actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!string.Equals(actual, pin.Sha256, StringComparison.Ordinal))
            {
                problems.Add($"{file} has SHA-256 {actual}, wanted {pin.Sha256}; delete it and fetch it again");
            }
        }

        return problems;
    }

    private static Dictionary<string, (string Sha256, long Bytes)> ReadLock(string lockPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(lockPath));
        Dictionary<string, (string, long)> pinned = new(StringComparer.Ordinal);
        if (!document.RootElement.TryGetProperty("models", out JsonElement models) || models.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{lockPath} has no \"models\" array; it must be the lock the fetch tool reads");
        }

        foreach (JsonElement model in models.EnumerateArray())
        {
            foreach (JsonElement file in model.GetProperty("files").EnumerateArray())
            {
                pinned[file.GetProperty("path").GetString()!] = (file.GetProperty("sha256").GetString()!, file.GetProperty("bytes").GetInt64());
            }
        }

        return pinned;
    }
}
