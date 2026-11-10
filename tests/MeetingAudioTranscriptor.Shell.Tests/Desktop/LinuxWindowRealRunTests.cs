// Drives the real Linux window with the real transcriber under a virtual display: a dropped recording
// is transcribed from the page's Start button into the three transcript files.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests.Desktop;

[Trait("Tier", "integration")]
[Trait("Category", "desktop")]
public sealed class LinuxWindowRealRunTests
{
    /// <summary>A short recording of the test set with two speakers.</summary>
    private const string Clip = "clips/fr/fr-multi-01.wav";

    [Fact]
    public async Task ADroppedRecordingIsTranscribedFromTheStartButton()
    {
        string shell = ShellLauncher.RequireLinuxWindow();
        string models = ModelCatalog.ResolveRoot();
        string granite = Path.Combine(models, "granite-speech-5.0-470m-turboctc", "model.int8.scores.onnx");
        Assert.SkipUnless(File.Exists(granite), $"needs the default models in {models}; run node build/fetch-models.mjs --into {models} or set {ModelCatalog.ModelsDirectoryVariable}");
        string source = Path.Combine(BenchData(), Clip);
        Assert.SkipUnless(File.Exists(source), $"needs the test set audio {source}; build it with node bench/testset/fetch-testset.mjs or set MAT_BENCH_DATA");

        string folder = Directory.CreateTempSubdirectory("mat-real-run-").FullName;
        try
        {
            // The transcripts are written beside the recording, so the recording is copied to a folder of its own.
            string recording = Path.Combine(folder, "meeting.wav");
            File.Copy(source, recording);
            string uri = new Uri(recording).AbsoluteUri;

            string script = """
                const post = (o) => window.webkit.messageHandlers.host.postMessage(JSON.stringify(o));
                const log = (s) => post({ automation: { log: s } });
                const seen = [];
                const pageReceive = window.__hostReceive;
                window.__hostReceive = (text) => { seen.push(JSON.parse(text)); pageReceive(text); };
                const until = async (test, what, seconds) => {
                  for (let i = 0; i < seconds * 20; i++) {
                    const found = test();
                    if (found) { return found; }
                    await new Promise((r) => setTimeout(r, 50));
                  }
                  throw new Error("timed out waiting for " + what);
                };
                (async () => {
                  post({ automation: { dropUris: ["
                """ + uri + """
                "] } });
                  await until(() => seen.find((m) => m.event === "file:selected"), "file:selected", 20);
                  const start = document.getElementById("start");
                  await until(() => !start.disabled, "Start to be enabled", 20);
                  start.click();
                  const finished = await until(() => seen.find((m) => m.event === "run:finished"), "run:finished", 240);
                  const stages = [...new Set(seen.filter((m) => m.event === "run:progress").map((m) => m.payload.stage))];
                  log("stages " + JSON.stringify(stages));
                  log("finished " + JSON.stringify(finished.payload));
                  await new Promise((r) => setTimeout(r, 200));
                  log("summary " + (document.getElementById("summary").hidden ? "hidden" : "shown"));
                  post({ automation: { exit: finished.payload.kind === "completed" ? 0 : 3 } });
                })().catch((e) => { log("failed " + e); post({ automation: { exit: 1 } }); });
                """;

            Dictionary<string, string> environment = new()
            {
                ["MAT_UI_DEMO"] = "0",
                [AutomationOptions.ExitAfterVariable] = "300000",
                [ModelCatalog.ModelsDirectoryVariable] = models,
            };
            ShellRun run = await ShellLauncher.RunAsync(shell, script, environment: environment, limit: TimeSpan.FromMinutes(5));

            Assert.True(run.ExitCode == 0, $"got exit code {run.ExitCode}, wanted 0 for a completed run\n{run.Transcript}");
            string[] log = [.. run.PageLog];
            string finishedLine = log.FirstOrDefault(line => line.StartsWith("finished ", StringComparison.Ordinal)) ?? "";
            Assert.True(finishedLine.Contains("\"kind\":\"completed\"", StringComparison.Ordinal), $"got '{finishedLine}', wanted a completed run\n{run.Transcript}");
            Assert.True(log.Contains("summary shown"), $"got the summary hidden, wanted the page to show the result\n{run.Transcript}");

            string[] expected = ["meeting.json", "meeting.txt", "meeting.vtt"];
            foreach (string name in expected)
            {
                Assert.True(File.Exists(Path.Combine(folder, name)), $"got no {name} beside the recording\n{run.Transcript}");
            }

            using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(folder, "meeting.json"), TestContext.Current.CancellationToken));
            int segments = json.RootElement.GetProperty("segments").GetArrayLength();
            int speakers = json.RootElement.GetProperty("speakers").GetArrayLength();
            Assert.True(segments >= 1 && speakers >= 1, $"got {segments} segments and {speakers} speakers, wanted at least one of each");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The test set folder: MAT_BENCH_DATA, else bench/data of the repository.</summary>
    private static string BenchData()
    {
        string? named = Environment.GetEnvironmentVariable("MAT_BENCH_DATA");
        if (!string.IsNullOrWhiteSpace(named))
        {
            return named;
        }

        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MeetingAudioTranscriptor.slnx")))
            {
                return Path.Combine(folder.FullName, "bench", "data");
            }
        }

        return Path.Combine("bench", "data");
    }
}
