// A transcriber that pretends: it simulates a five second run through the stages and writes nothing,
// so the page can be developed and shown without the models.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Switches the demo transcriber on from the environment.</summary>
public static class DemoMode
{
    /// <summary>The environment variable that turns the demo on when set to 1.</summary>
    public const string VariableName = "MAT_UI_DEMO";

    /// <summary>The demo transcriber when <c>MAT_UI_DEMO=1</c>; otherwise null, and the host uses the real one.</summary>
    public static ITranscriber? CreateIfRequested()
    {
        return Environment.GetEnvironmentVariable(VariableName) == "1" ? new DemoTranscriber(TimeSpan.FromSeconds(5)) : null;
    }
}

/// <summary>Simulates a run for interface work; reports the models as installed and writes no file.</summary>
internal sealed class DemoTranscriber : ITranscriber
{
    private static readonly string[] Stages = BridgeStages.All;

    private readonly TimeSpan _total;

    public DemoTranscriber(TimeSpan total)
    {
        _total = total;
    }

    public ModelCheckResult CheckModels()
    {
        return new ModelCheckResult { Ready = true };
    }

    public async Task<TranscribeOutcome> RunAsync(TranscribeRequest request, IProgress<TranscribeProgress> progress, CancellationToken cancellation)
    {
        const int stepsPerStage = 10;
        TimeSpan step = _total / (Stages.Length * stepsPerStage);
        for (int stage = 0; stage < Stages.Length; stage++)
        {
            for (int tick = 0; tick < stepsPerStage; tick++)
            {
                cancellation.ThrowIfCancellationRequested();
                progress.Report(new TranscribeProgress
                {
                    Stage = Stages[stage],
                    Fraction = (stage * stepsPerStage + tick) / (double)(Stages.Length * stepsPerStage),
                });
                await Task.Delay(step, cancellation).ConfigureAwait(false);
            }
        }

        progress.Report(new TranscribeProgress { Stage = Stages[^1], Fraction = 1.0 });
        string folder = request.OutputFolder ?? Path.GetDirectoryName(request.InputPath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(request.InputPath);
        return new TranscribeOutcome
        {
            Kind = TranscribeOutcomeKinds.Completed,
            Message = "Demo run finished. No file was written.",
            OutputFolder = folder,
            OutputFiles = request.Formats.Select(format => Path.Combine(folder, baseName + "." + format)).ToList(),
            DurationSeconds = 600,
            ProcessingSeconds = _total.TotalSeconds,
            SpeedFactor = 600 / Math.Max(_total.TotalSeconds, 0.001),
            Speakers = request.SpeakerSeparation ? request.SpeakerCount ?? 3 : 1,
            Words = 1234,
        };
    }
}
