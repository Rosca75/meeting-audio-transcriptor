// The help the executable prints, for the program and for each command.
using MeetingAudioTranscriptor.Contracts;

namespace MeetingAudioTranscriptor.Cli;

/// <summary>The help texts, one per topic.</summary>
internal static class HelpText
{
    public const string Executable = AppInfo.Name + "-cli";

    public static string For(string? topic) => topic switch
    {
        "transcribe" => Transcribe,
        "benchmark" => Benchmark,
        "models" => Models,
        _ => Program,
    };

    private const string Program =
        "Transcribes a recorded meeting (English, French or German) into transcript files, entirely on this machine.\n"
        + "\n"
        + "Usage:\n"
        + "  " + Executable + " transcribe <file> --language en|fr|de [options]\n"
        + "  " + Executable + " benchmark [options]\n"
        + "  " + Executable + " models [--models <dir>] [--verify]\n"
        + "  " + Executable + " --version\n"
        + "  " + Executable + " <command> --help\n"
        + "\n"
        + "Exit codes: 0 done, 1 usage error, 2 input file problem, 3 models missing, 4 output problem,\n"
        + "5 unexpected failure, 130 cancelled.\n";

    private const string Transcribe =
        "Usage: " + Executable + " transcribe <file> --language en|fr|de [options]\n"
        + "\n"
        + "Reads a .wav, .m4a or .mp4 recording and writes its transcript beside it, named after it.\n"
        + "\n"
        + "Options:\n"
        + "  --language en|fr|de   the language spoken (required)\n"
        + "  --formats <list>      the formats to write, any of vtt,json,txt (default: all three)\n"
        + "  --output <dir>        the folder to write to (default: the recording's folder)\n"
        + "  --no-speakers         do not tell speakers apart\n"
        + "  --speakers <n>        the number of speakers, when known\n"
        + "  --threads <n>         recognition threads (default: the logical cores, at most 4)\n"
        + "  --raw                 also write .raw.vtt and .raw.txt with the text before cleaning\n"
        + "  --models <dir>        the models folder (default: MAT_MODELS_DIR, or models beside the program)\n"
        + "  --quiet               print nothing but errors\n"
        + "\n"
        + "Ctrl+C cancels and leaves no partial file.\n";

    private const string Benchmark =
        "Usage: " + Executable + " benchmark [options]\n"
        + "\n"
        + "Transcribes the test set and reports word error rates, cleaning retention, words found in noise,\n"
        + "speaker error rates, speed and memory, then writes <out>/<yyyy-MM-dd>-<label>.json.\n"
        + "\n"
        + "Options:\n"
        + "  --data <dir>                 the test set (default: MAT_BENCH_DATA, or ./bench/data)\n"
        + "  --set <name>                 clips, long, containers, noise, all, or a file id; repeat or separate with commas\n"
        + "                               (default: clips)\n"
        + "  --long-file <path> --language <code>\n"
        + "                               also time a recording outside the test set; repeatable\n"
        + "  --threads <n>                recognition threads (default: the logical cores, at most 4)\n"
        + "  --no-speakers                do not tell speakers apart\n"
        + "  --speakers-hint-from-reference\n"
        + "                               also run each file that has reference turns with their speaker\n"
        + "                               count as the hint, reported as a second speaker variant\n"
        + "  --repeat <n>                 run each file n times and report the median speed (default: 1)\n"
        + "  --out <dir>                  where the result file goes (default: ./bench/results)\n"
        + "  --label <text>               the result file's label (default: the sets' names)\n"
        + "  --models <dir>               the models folder\n";

    private const string Models =
        "Usage: " + Executable + " models [--models <dir>] [--verify]\n"
        + "\n"
        + "Lists the model files a transcription needs and whether each is installed.\n"
        + "\n"
        + "Options:\n"
        + "  --models <dir>   the models folder (default: MAT_MODELS_DIR, or models beside the program)\n"
        + "  --verify         also check each file's size and SHA-256 against models.lock\n";
}
