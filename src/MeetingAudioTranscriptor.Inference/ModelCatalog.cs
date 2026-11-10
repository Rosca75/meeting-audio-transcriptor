// Maps a model name and a file role to a path in the models folder, and checks the files against build/models.lock.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MeetingAudioTranscriptor.Inference;

/// <summary>One file of a model, as <c>build/models.lock</c> pins it.</summary>
/// <param name="Path">The path relative to the models folder, with forward slashes.</param>
/// <param name="Sha256">The lower-case hex SHA-256 of the file.</param>
/// <param name="Bytes">The size of the file.</param>
/// <param name="Role">What the file is for: model, tokenizer, config or auxiliary.</param>
public sealed record CatalogFile(string Path, string Sha256, long Bytes, string Role);

/// <summary>One model of the lock: its name, whether a release ships it and its files.</summary>
/// <param name="Name">The model's name, the key every lookup uses.</param>
/// <param name="Set">default for a model a release ships, candidate for one only benchmarks use.</param>
/// <param name="Optional">True when the application works without the model.</param>
/// <param name="Files">The files the model consists of.</param>
public sealed record CatalogModel(string Name, string Set, bool Optional, IReadOnlyList<CatalogFile> Files);

/// <summary>
/// Resolves the files of each model inside one models folder and reports what is missing.
/// </summary>
/// <remarks>
/// The catalog holds no model knowledge of its own: the description of the model set is the lock
/// file, which the fetch tool installs from and the release pipeline packages from, so the three
/// cannot disagree about a path. The application ships the models folder beside the executable.
/// </remarks>
public sealed class ModelCatalog
{
    /// <summary>The environment variable that points development runs and the test suites at a models folder.</summary>
    public const string ModelsDirectoryVariable = "MAT_MODELS_DIR";

    private const string FetchCommand = "node build/fetch-models.mjs";
    private readonly Dictionary<string, CatalogModel> _models;

    /// <summary>Creates a catalog over a folder and the models it should hold.</summary>
    public ModelCatalog(string modelsRoot, IEnumerable<CatalogModel> models)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsRoot);
        ArgumentNullException.ThrowIfNull(models);
        Root = Path.GetFullPath(modelsRoot);
        _models = models.ToDictionary(model => model.Name, StringComparer.Ordinal);
    }

    /// <summary>The models folder.</summary>
    public string Root { get; }

    /// <summary>The model names the catalog knows.</summary>
    public IReadOnlyCollection<string> ModelNames => _models.Keys;

    /// <summary>Reads a lock file and builds the catalog over a models folder.</summary>
    public static ModelCatalog FromLockFile(string modelsRoot, string lockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        if (!File.Exists(lockPath))
        {
            throw new FileNotFoundException($"the model lock {lockPath} does not exist; pass the path of build/models.lock from the repository, or of the copy the release package carries");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(lockPath));
        if (!document.RootElement.TryGetProperty("models", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{lockPath} has no \"models\" array; it must be the lock the fetch tool reads");
        }

        List<CatalogModel> models = [];
        foreach (JsonElement model in array.EnumerateArray())
        {
            List<CatalogFile> files = [];
            foreach (JsonElement file in model.GetProperty("files").EnumerateArray())
            {
                files.Add(new CatalogFile(
                    file.GetProperty("path").GetString()!,
                    file.GetProperty("sha256").GetString()!,
                    file.GetProperty("bytes").GetInt64(),
                    file.GetProperty("role").GetString()!));
            }

            bool optional = model.TryGetProperty("optional", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
            models.Add(new CatalogModel(model.GetProperty("name").GetString()!, model.GetProperty("set").GetString()!, optional, files));
        }

        return new ModelCatalog(modelsRoot, models);
    }

    /// <summary>
    /// The models folder to use: <c>MAT_MODELS_DIR</c> when set, else a <c>models</c> folder beside
    /// the executable, else the <c>models</c> folder of the repository the executable was built in.
    /// </summary>
    public static string ResolveRoot()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(ModelsDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        string beside = Path.Combine(AppContext.BaseDirectory, "models");
        if (Directory.Exists(beside))
        {
            return beside;
        }

        // A development build runs from bin/<configuration>/<framework>: walk up to the repository.
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MeetingAudioTranscriptor.slnx")))
            {
                return Path.Combine(folder.FullName, "models");
            }
        }

        return beside;
    }

    /// <summary>The absolute path of the one file a model has in a role.</summary>
    /// <exception cref="ArgumentException">The model is unknown, or has no file or several files in the role.</exception>
    /// <exception cref="FileNotFoundException">The file is not in the models folder.</exception>
    public string ResolveRole(string modelName, string role)
    {
        CatalogModel model = Find(modelName);
        CatalogFile[] matches = model.Files.Where(file => string.Equals(file.Role, role, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            throw new ArgumentException(
                $"the model '{modelName}' has {matches.Length} file(s) with role '{role}', wanted exactly one; "
                + $"its files are: {string.Join(", ", model.Files.Select(file => $"{file.Path} ({file.Role})"))}. Use ResolveFile with a file name.");
        }

        return Existing(model, matches[0]);
    }

    /// <summary>The absolute path of a model's file, named by its full relative path or by its file name alone.</summary>
    /// <exception cref="ArgumentException">The model is unknown or has no such file.</exception>
    /// <exception cref="FileNotFoundException">The file is not in the models folder.</exception>
    public string ResolveFile(string modelName, string fileName)
    {
        CatalogModel model = Find(modelName);
        CatalogFile? match = model.Files.FirstOrDefault(file =>
            string.Equals(file.Path, fileName, StringComparison.Ordinal)
            || string.Equals(System.IO.Path.GetFileName(file.Path), fileName, StringComparison.Ordinal));
        if (match is null)
        {
            throw new ArgumentException($"the model '{modelName}' has no file '{fileName}'; its files are: {string.Join(", ", model.Files.Select(file => file.Path))}.");
        }

        return Existing(model, match);
    }

    /// <summary>The relative paths of a model's files that are not present, empty when it is fully installed.</summary>
    public IReadOnlyList<string> MissingFiles(string modelName)
    {
        CatalogModel model = Find(modelName);
        return model.Files.Where(file => !File.Exists(AbsolutePath(file))).Select(file => file.Path).ToArray();
    }

    /// <summary>Whether every file of a model is present (sizes only, which is cheap).</summary>
    public bool IsInstalled(string modelName) =>
        Find(modelName).Files.All(file => File.Exists(AbsolutePath(file)) && new FileInfo(AbsolutePath(file)).Length == file.Bytes);

    /// <summary>
    /// Reads every file of the named models (all models when none is named) and compares size and
    /// SHA-256 with the lock. Slow on the large files, so it is called on demand, never at start-up.
    /// </summary>
    /// <returns>One sentence per problem; empty when everything matches.</returns>
    public IReadOnlyList<string> Verify(params string[] modelNames)
    {
        IEnumerable<CatalogModel> selected = modelNames.Length == 0 ? _models.Values : modelNames.Select(Find);
        List<string> problems = [];
        foreach (CatalogModel model in selected)
        {
            foreach (CatalogFile file in model.Files)
            {
                string path = AbsolutePath(file);
                if (!File.Exists(path))
                {
                    problems.Add($"{file.Path} is missing; run {FetchCommand} --model {model.Name} --into {Root}");
                    continue;
                }

                long length = new FileInfo(path).Length;
                if (length != file.Bytes)
                {
                    problems.Add($"{file.Path} has {length} bytes, wanted {file.Bytes}; delete it and run {FetchCommand} --model {model.Name} --into {Root}");
                    continue;
                }

                using FileStream stream = File.OpenRead(path);
                string actual = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (!string.Equals(actual, file.Sha256, StringComparison.Ordinal))
                {
                    problems.Add($"{file.Path} has sha256 {actual}, wanted {file.Sha256}; delete it and run {FetchCommand} --model {model.Name} --into {Root}");
                }
            }
        }

        return problems;
    }

    private CatalogModel Find(string modelName)
    {
        if (!_models.TryGetValue(modelName, out CatalogModel? model))
        {
            throw new ArgumentException($"the catalog has no model '{modelName}'; the models are: {string.Join(", ", _models.Keys)}.");
        }

        return model;
    }

    private string AbsolutePath(CatalogFile file) => System.IO.Path.Combine(Root, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));

    private string Existing(CatalogModel model, CatalogFile file)
    {
        string path = AbsolutePath(file);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"the file {file.Path} of model '{model.Name}' is not in {Root}; fetch it with {FetchCommand} --model {model.Name} --into {Root} "
                + $"or point {ModelsDirectoryVariable} at a folder that holds it",
                path);
        }

        return path;
    }
}
