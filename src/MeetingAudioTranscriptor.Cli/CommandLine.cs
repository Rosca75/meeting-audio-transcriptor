// Reads the command line into one of the commands, or into a usage error that names the bad option and its valid values.
using System;
using System.Collections.Generic;
using System.Globalization;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Writers;

namespace MeetingAudioTranscriptor.Cli;

/// <summary>The commands the executable knows.</summary>
internal enum CommandKind
{
    /// <summary>Print help, for the program or for one command.</summary>
    Help,

    /// <summary>Print the version.</summary>
    Version,

    /// <summary>Transcribe one recording.</summary>
    Transcribe,

    /// <summary>Run the benchmark over the test set.</summary>
    Benchmark,

    /// <summary>List and verify the model files.</summary>
    Models,

    /// <summary>The command line could not be read.</summary>
    UsageError,
}

/// <summary>The options of <c>transcribe</c>.</summary>
internal sealed record TranscribeArguments
{
    public required string File { get; init; }

    public required string Language { get; init; }

    public IReadOnlyList<TranscriptFormat> Formats { get; init; } = [TranscriptFormat.Vtt, TranscriptFormat.Json, TranscriptFormat.Txt];

    public string? Output { get; init; }

    public bool NoSpeakers { get; init; }

    public int? Speakers { get; init; }

    public int? Threads { get; init; }

    public bool Raw { get; init; }

    public string? Models { get; init; }

    public bool Quiet { get; init; }
}

/// <summary>A recording given to the benchmark outside the test set, with its language.</summary>
internal sealed record ExtraFile(string Path, string Language);

/// <summary>The options of <c>benchmark</c>.</summary>
internal sealed record BenchmarkArguments
{
    public string? Data { get; init; }

    public IReadOnlyList<string> Sets { get; init; } = [];

    public IReadOnlyList<ExtraFile> ExtraFiles { get; init; } = [];

    public int? Threads { get; init; }

    public bool NoSpeakers { get; init; }

    /// <summary>Also runs every file that has reference turns with their speaker count as the hint, reported as a second variant.</summary>
    public bool SpeakersHintFromReference { get; init; }

    public int Repeat { get; init; } = 1;

    public string? Out { get; init; }

    public string? Label { get; init; }

    public string? Models { get; init; }
}

/// <summary>The options of <c>models</c>.</summary>
internal sealed record ModelsArguments(string? Models, bool Verify);

/// <summary>What the command line asked for.</summary>
internal sealed record ParsedCommandLine(CommandKind Kind)
{
    /// <summary>For help: the command whose help to print, or null for the program's.</summary>
    public string? Topic { get; init; }

    /// <summary>For a usage error: what was wrong and how to fix it.</summary>
    public string? Error { get; init; }

    public TranscribeArguments? Transcribe { get; init; }

    public BenchmarkArguments? Benchmark { get; init; }

    public ModelsArguments? Models { get; init; }
}

/// <summary>The hand-written parser: <c>--name value</c> or <c>--name=value</c>, flags without a value, positional arguments in order.</summary>
internal static class CommandLine
{
    /// <summary>The command names, in the order help lists them.</summary>
    public static readonly string[] Commands = ["transcribe", "benchmark", "models"];

    /// <summary>The benchmark's named sets.</summary>
    public static readonly string[] BenchmarkSets = ["clips", "long", "containers", "noise", "all"];

    public static ParsedCommandLine Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new ParsedCommandLine(CommandKind.Help);
        }

        string first = args[0];
        switch (first)
        {
            case "-h" or "--help" or "help":
                return new ParsedCommandLine(CommandKind.Help) { Topic = args.Count > 1 && Array.IndexOf(Commands, args[1]) >= 0 ? args[1] : null };
            case "--version" or "-V" or "version":
                return new ParsedCommandLine(CommandKind.Version);
        }

        if (Array.IndexOf(Commands, first) < 0)
        {
            return Error($"'{first}' is not a command; use one of {string.Join(", ", Commands)}, or --help", null);
        }

        Tokens tokens;
        try
        {
            tokens = Tokens.Read(args, 1);
        }
        catch (UsageException e)
        {
            return Error(e.Message, first);
        }

        if (tokens.Help)
        {
            return new ParsedCommandLine(CommandKind.Help) { Topic = first };
        }

        try
        {
            return first switch
            {
                "transcribe" => new ParsedCommandLine(CommandKind.Transcribe) { Transcribe = ParseTranscribe(tokens) },
                "benchmark" => new ParsedCommandLine(CommandKind.Benchmark) { Benchmark = ParseBenchmark(tokens) },
                _ => new ParsedCommandLine(CommandKind.Models) { Models = ParseModels(tokens) },
            };
        }
        catch (UsageException e)
        {
            return Error(e.Message, first);
        }
    }

    private static ParsedCommandLine Error(string message, string? topic) => new(CommandKind.UsageError) { Error = message, Topic = topic };

    private static TranscribeArguments ParseTranscribe(Tokens tokens)
    {
        if (tokens.Positional.Count == 0)
        {
            throw new UsageException("transcribe needs the recording to read: transcribe <file> --language en|fr|de");
        }

        if (tokens.Positional.Count > 1)
        {
            throw new UsageException($"transcribe reads one recording, got {tokens.Positional.Count}: {string.Join(" ", tokens.Positional)}; quote a path that holds spaces");
        }

        TranscribeArguments parsed = new()
        {
            File = tokens.Positional[0],
            Language = Language(tokens.Required("--language", "en|fr|de")),
        };

        foreach ((string name, string? value) in tokens.Options)
        {
            parsed = name switch
            {
                "--language" => parsed,
                "--formats" => parsed with { Formats = Formats(Value(name, value, "vtt,json,txt")) },
                "--output" => parsed with { Output = Value(name, value, "<dir>") },
                "--no-speakers" => parsed with { NoSpeakers = Flag(name, value) },
                "--speakers" => parsed with { Speakers = Count(name, Value(name, value, "<n>"), 1, 32) },
                "--threads" => parsed with { Threads = Count(name, Value(name, value, "<n>"), 1, 256) },
                "--raw" => parsed with { Raw = Flag(name, value) },
                "--models" => parsed with { Models = Value(name, value, "<dir>") },
                "--quiet" => parsed with { Quiet = Flag(name, value) },
                _ => throw Unknown(name, "transcribe", "--language, --formats, --output, --no-speakers, --speakers, --threads, --raw, --models, --quiet"),
            };
        }

        if (parsed.NoSpeakers && parsed.Speakers is not null)
        {
            throw new UsageException("--speakers states how many speakers to tell apart, which --no-speakers switches off; pass one of the two");
        }

        return parsed;
    }

    private static BenchmarkArguments ParseBenchmark(Tokens tokens)
    {
        if (tokens.Positional.Count > 0)
        {
            throw new UsageException($"benchmark takes no file argument, got '{tokens.Positional[0]}'; pass a test set with --set or a recording with --long-file <path> --language <code>");
        }

        BenchmarkArguments parsed = new();
        List<string> sets = [];
        List<ExtraFile> extras = [];
        string? pendingFile = null;
        foreach ((string name, string? value) in tokens.Options)
        {
            if (pendingFile is not null && name != "--language")
            {
                throw new UsageException($"--long-file {pendingFile} needs its language right after it: --long-file <path> --language en|fr|de");
            }

            switch (name)
            {
                case "--data":
                    parsed = parsed with { Data = Value(name, value, "<dir>") };
                    break;
                case "--set":
                    foreach (string set in Value(name, value, "clips|long|containers|noise|all|<file id>").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        sets.Add(set);
                    }

                    break;
                case "--long-file":
                    pendingFile = Value(name, value, "<path>");
                    break;
                case "--language":
                    if (pendingFile is null)
                    {
                        throw new UsageException("--language in a benchmark gives the language of the --long-file just before it: --long-file <path> --language en|fr|de");
                    }

                    extras.Add(new ExtraFile(pendingFile, Language(Value(name, value, "en|fr|de"))));
                    pendingFile = null;
                    break;
                case "--threads":
                    parsed = parsed with { Threads = Count(name, Value(name, value, "<n>"), 1, 256) };
                    break;
                case "--no-speakers":
                    parsed = parsed with { NoSpeakers = Flag(name, value) };
                    break;
                case "--speakers-hint-from-reference":
                    parsed = parsed with { SpeakersHintFromReference = Flag(name, value) };
                    break;
                case "--repeat":
                    parsed = parsed with { Repeat = Count(name, Value(name, value, "<n>"), 1, 100) };
                    break;
                case "--out":
                    parsed = parsed with { Out = Value(name, value, "<dir>") };
                    break;
                case "--label":
                    parsed = parsed with { Label = Label(Value(name, value, "<text>")) };
                    break;
                case "--models":
                    parsed = parsed with { Models = Value(name, value, "<dir>") };
                    break;
                default:
                    throw Unknown(name, "benchmark", "--data, --set, --long-file, --language, --threads, --no-speakers, --speakers-hint-from-reference, --repeat, --out, --label, --models");
            }
        }

        if (pendingFile is not null)
        {
            throw new UsageException($"--long-file {pendingFile} needs its language right after it: --long-file <path> --language en|fr|de");
        }

        if (parsed.NoSpeakers && parsed.SpeakersHintFromReference)
        {
            throw new UsageException("--speakers-hint-from-reference runs speaker separation with a hint, which --no-speakers switches off; pass one of the two");
        }

        if (sets.Count == 0 && extras.Count == 0)
        {
            sets.Add("clips");
        }

        return parsed with { Sets = sets, ExtraFiles = extras };
    }

    private static ModelsArguments ParseModels(Tokens tokens)
    {
        if (tokens.Positional.Count > 0)
        {
            throw new UsageException($"models takes no file argument, got '{tokens.Positional[0]}'; pass the folder with --models <dir>");
        }

        string? models = null;
        bool verify = false;
        foreach ((string name, string? value) in tokens.Options)
        {
            switch (name)
            {
                case "--models":
                    models = Value(name, value, "<dir>");
                    break;
                case "--verify":
                    verify = Flag(name, value);
                    break;
                default:
                    throw Unknown(name, "models", "--models, --verify");
            }
        }

        return new ModelsArguments(models, verify);
    }

    private static string Language(string value)
    {
        if (!CleaningLanguages.TryParse(value, out CleaningLanguage language))
        {
            throw new UsageException($"--language '{value}' is not supported; use en, fr or de");
        }

        return CleaningLanguages.Code(language);
    }

    private static TranscriptFormat[] Formats(string value)
    {
        List<TranscriptFormat> formats = [];
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TranscriptFormats.TryParse(part, out TranscriptFormat format))
            {
                throw new UsageException($"--formats '{part}' is not a transcript format; use any of vtt, json and txt, separated by commas");
            }

            if (!formats.Contains(format))
            {
                formats.Add(format);
            }
        }

        if (formats.Count == 0)
        {
            throw new UsageException("--formats names no format; use any of vtt, json and txt, separated by commas");
        }

        return formats.ToArray();
    }

    private static int Count(string name, string value, int minimum, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < minimum || count > maximum)
        {
            throw new UsageException($"{name} '{value}' is not a whole number from {minimum} to {maximum}");
        }

        return count;
    }

    private static string Label(string value)
    {
        foreach (char c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                throw new UsageException($"--label '{value}' becomes part of a file name; use letters, digits, '-', '_' and '.' only");
            }
        }

        return value;
    }

    private static string Value(string name, string? value, string expected)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new UsageException($"{name} needs a value: {name} {expected}");
        }

        return value;
    }

    private static bool Flag(string name, string? value)
    {
        if (value is not null)
        {
            throw new UsageException($"{name} is a switch and takes no value; write {name} alone");
        }

        return true;
    }

    private static UsageException Unknown(string name, string command, string valid) =>
        new($"'{name}' is not an option of {command}; its options are {valid}");

    /// <summary>The arguments after the command, split into options in order and positional arguments.</summary>
    private sealed class Tokens
    {
        /// <summary>The options that take no value; every other option takes the next argument.</summary>
        private static readonly HashSet<string> Switches = new(StringComparer.Ordinal) { "--no-speakers", "--raw", "--quiet", "--verify", "--speakers-hint-from-reference" };

        public List<(string Name, string? Value)> Options { get; } = [];

        public List<string> Positional { get; } = [];

        public bool Help { get; private set; }

        public static Tokens Read(IReadOnlyList<string> args, int start)
        {
            Tokens tokens = new();
            for (int i = start; i < args.Count; i++)
            {
                string arg = args[i];
                if (arg is "-h" or "--help")
                {
                    tokens.Help = true;
                    continue;
                }

                if (arg == "--")
                {
                    for (i++; i < args.Count; i++)
                    {
                        tokens.Positional.Add(args[i]);
                    }

                    break;
                }

                if (!arg.StartsWith('-') || arg == "-")
                {
                    tokens.Positional.Add(arg);
                    continue;
                }

                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new UsageException($"'{arg}' is not an option; options are written with two dashes, such as --language");
                }

                int equals = arg.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0)
                {
                    tokens.Options.Add((arg[..equals], arg[(equals + 1)..]));
                }
                else if (Switches.Contains(arg))
                {
                    tokens.Options.Add((arg, null));
                }
                else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    tokens.Options.Add((arg, args[++i]));
                }
                else
                {
                    tokens.Options.Add((arg, null));
                }
            }

            return tokens;
        }

        /// <summary>The value of a required option; throws naming it when absent.</summary>
        public string Required(string name, string expected)
        {
            foreach ((string option, string? value) in Options)
            {
                if (option == name && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            throw new UsageException($"{name} is required: {name} {expected}");
        }
    }
}

/// <summary>A command line that cannot be read; the message is shown to the user as it stands.</summary>
internal sealed class UsageException : Exception
{
    public UsageException()
    {
    }

    public UsageException(string message)
        : base(message)
    {
    }

    public UsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
