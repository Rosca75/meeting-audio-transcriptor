// Drives the real Linux window under a virtual display: the embedded page loads, the bridge answers
// through the page's own transport, a drop reaches the bridge, a demo run ends with its terminal
// event, and nothing off the page's origin is ever loaded.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests.Desktop;

[Trait("Tier", "integration")]
[Trait("Category", "desktop")]
public sealed class LinuxWindowTests
{
    /// <summary>
    /// The script's shared head: it posts through the page's transport, records every envelope the
    /// host delivers (and still hands it to the page's own receiver), and waits for conditions.
    /// </summary>
    private const string Prelude = """
        const post = (o) => window.webkit.messageHandlers.host.postMessage(JSON.stringify(o));
        const log = (s) => post({ automation: { log: s } });
        const seen = [];
        const pageReceive = window.__hostReceive;
        window.__hostReceive = (text) => { seen.push(JSON.parse(text)); pageReceive(text); };
        const until = async (test, what) => {
          for (let i = 0; i < 400; i++) {
            const found = test();
            if (found) { return found; }
            await new Promise((r) => setTimeout(r, 50));
          }
          throw new Error("timed out waiting for " + what);
        };
        const pause = (ms) => new Promise((r) => setTimeout(r, ms));
        const finish = (body) => body().then(() => post({ automation: { exit: 0 } }), (e) => { log("failed " + e); post({ automation: { exit: 1 } }); });
        """;

    /// <summary>A string holding every character class the literal escaping must survive.</summary>
    private const string Tricky = "q\\\" b\\\\ ls\\u2028ps\\u2029 e\\u00e9 \\ud83d\\ude00 nl\\n";

    [Fact]
    public async Task ThePageAnswersADropAndADemoRunToItsTerminalEvent()
    {
        string shell = ShellLauncher.RequireLinuxWindow();
        string folder = Directory.CreateTempSubdirectory("mat-drop-").FullName;
        try
        {
            // A real file with a space and a non-ASCII letter, dropped as a percent-encoded URI.
            string recording = Path.Combine(folder, "réunion one.wav");
            await File.WriteAllBytesAsync(recording, [], TestContext.Current.CancellationToken);
            string uri = "file://" + string.Join("/", recording.Split('/').Select(Uri.EscapeDataString));

            string script = Prelude + $$"""
                finish(async () => {
                  post({ id: 9001, method: "app.info", args: [] });
                  const info = await until(() => seen.find((m) => m.id === 9001), "the app.info reply");
                  log("info " + JSON.stringify(info.result));
                  post({ automation: { echo: "{{Tricky}}" } });
                  const echo = await until(() => seen.find((m) => m.automation), "the echo");
                  log("echo " + (echo.automation.echo === "{{Tricky}}" ? "intact" : "changed"));
                  post({ automation: { dropUris: ["https://example.com/x.wav", "{{uri}}"] } });
                  const selected = await until(() => seen.find((m) => m.event === "file:selected" || m.event === "file:refused"), "the file event");
                  log("file " + JSON.stringify(selected));
                  await pause(100);
                  log("shown " + document.getElementById("file-name").textContent);
                  const start = document.getElementById("start");
                  await until(() => !start.disabled, "Start to be enabled");
                  start.click();
                  const finished = await until(() => seen.find((m) => m.event === "run:finished"), "run:finished");
                  log("progress " + seen.filter((m) => m.event === "run:progress").length);
                  log("finished " + finished.payload.kind);
                  await pause(100);
                  log("summary " + (document.getElementById("summary").hidden ? "hidden" : "shown"));
                });
                """;

            ShellRun run = await ShellLauncher.RunAsync(shell, script);

            Assert.True(run.ExitCode == 0, $"got exit code {run.ExitCode}, wanted 0\n{run.Transcript}");
            string[] log = [.. run.PageLog];
            Assert.True(log.Any(line => line.StartsWith("info {\"version\":", StringComparison.Ordinal) && line.Contains("\"ready\":true", StringComparison.Ordinal)), $"got no app.info reply with ready models, wanted one\n{run.Transcript}");
            Assert.True(log.Contains("echo intact"), $"got no intact echo, wanted the escaped text to reach the page unchanged\n{run.Transcript}");
            Assert.True(run.Output.Any(line => line.StartsWith("automation: drop forwarded ", StringComparison.Ordinal) && !line.Contains("example.com", StringComparison.Ordinal)), $"got no drop forwarded without the web address, wanted the file path alone\n{run.Transcript}");
            Assert.True(log.Any(line => line.StartsWith("file {\"event\":\"file:selected\"", StringComparison.Ordinal) && line.Contains("réunion one.wav", StringComparison.Ordinal)), $"got no file:selected naming the dropped file, wanted one\n{run.Transcript}");
            Assert.True(log.Any(line => line.StartsWith("shown ", StringComparison.Ordinal) && line.Contains("réunion one.wav", StringComparison.Ordinal)), $"got the page not showing the dropped file, wanted its name on screen\n{run.Transcript}");
            Assert.True(log.Contains("finished completed"), $"got no completed run:finished, wanted the demo run to end\n{run.Transcript}");
            Assert.True(log.Contains("summary shown"), $"got the summary hidden, wanted the page to show the result\n{run.Transcript}");
            Assert.True(log.Any(line => line.StartsWith("progress ", StringComparison.Ordinal) && int.Parse(line["progress ".Length..], System.Globalization.CultureInfo.InvariantCulture) > 0), $"got no progress event, wanted some\n{run.Transcript}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task NothingOffTheOriginIsLoadedOrNavigatedTo()
    {
        string shell = ShellLauncher.RequireLinuxWindow();
        string script = Prelude + """
            const image = (src) => new Promise((resolve) => {
              const img = new Image();
              img.onload = () => resolve("loaded");
              img.onerror = () => resolve("refused");
              img.src = src;
            });
            finish(async () => {
              log("missing " + await image("/missing.png"));
              log("notes " + await image("/BRIDGE.md"));
              log("remote image " + await image("https://example.com/x.png"));
              let fetched = "allowed";
              try { await fetch("/index.html"); } catch (e) { fetched = "refused"; }
              log("fetch " + fetched);
              const inline = document.createElement("script");
              inline.textContent = "window.__inlineRan = true;";
              document.head.appendChild(inline);
              log("inline script " + (window.__inlineRan ? "ran" : "refused"));
              window.open("https://example.com/popup");
              location.href = "https://example.com/";
              await pause(1000);
              log("still on " + location.href);
            });
            """;

        ShellRun run = await ShellLauncher.RunAsync(shell, script);

        Assert.True(run.ExitCode == 0, $"got exit code {run.ExitCode}, wanted 0\n{run.Transcript}");
        string[] log = [.. run.PageLog];
        foreach (string wanted in new[] { "missing refused", "notes refused", "remote image refused", "fetch refused", "inline script refused", "still on app://meeting-audio-transcriptor/" })
        {
            Assert.True(log.Contains(wanted), $"got no '{wanted}', wanted it\n{run.Transcript}");
        }

        Assert.True(run.Output.Contains("automation: asset 404 app://meeting-audio-transcriptor/missing.png"), $"got no 404 for the missing file from the host, wanted one\n{run.Transcript}");
        Assert.True(run.Output.Contains("automation: navigation blocked https://example.com/"), $"got no blocked navigation from the policy handler, wanted one\n{run.Transcript}");
        Assert.True(!run.Output.Any(line => line.StartsWith("automation: asset ", StringComparison.Ordinal) && !line.Contains(" app://meeting-audio-transcriptor/", StringComparison.Ordinal)), $"got an asset request off the origin, wanted none\n{run.Transcript}");
    }

    [Fact]
    public async Task WithoutADisplayTheWindowExitsWithTwoAndSaysWhy()
    {
        string shell = ShellLauncher.RequireLinuxWindow();

        ShellRun run = await ShellLauncher.RunAsync(shell, script: null, virtualDisplay: false);

        Assert.True(run.ExitCode == ShellExitCodes.RuntimeMissing, $"got exit code {run.ExitCode}, wanted {ShellExitCodes.RuntimeMissing}\n{run.Transcript}");
        Assert.True(run.Error.Contains("no display could be opened", StringComparison.Ordinal), $"got no display message, wanted one\n{run.Transcript}");
    }
}
