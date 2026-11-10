// Models-tier floors of speaker separation through the full pipeline (real VAD, real recogniser, no
// reference speech regions): the speaker count of the ten-minute files and the constructed clips,
// with and without the hint, the meeting clips within one, and attribution floors under the measured values.
using System;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Diarisation;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

[Trait("Category", "diarisation")]
[Trait("Tier", "models")]
[Collection(DiarisationSequence.Name)]
public sealed class PipelineDiarisationTests
{
    public static TheoryData<string> LongFiles => ["en-10min", "fr-10min", "de-10min"];

    public static TheoryData<string> ConstructedClips => ["en-multi-01", "fr-multi-01", "fr-multi-02", "fr-multi-03", "de-multi-01", "de-multi-02", "de-multi-03"];

    public static TheoryData<string> MeetingClips => ["en-ami-01", "en-ami-02", "en-ami-sdm-01"];

    private static PipelineRun Run(string id)
    {
        string? skip = PipelineDiarisation.SkipReason();
        Assert.SkipWhen(skip is not null, skip ?? "");
        PipelineRun? run = PipelineDiarisation.Run(id);
        Assert.SkipWhen(run is null, $"needs {id}.wav and {id}.rttm in {DiarisationBench.DataDirectory}; run: node bench/testset/fetch-testset.mjs");
        return run!;
    }

    private static string Show(PipelineScore score) =>
        $"{score.Run.Id}: {score.EstimatedSpeakers} speakers for {score.Run.TrueSpeakers}, attribution {DiarisationBench.F(score.Attribution)}, "
        + $"speaking seconds [{string.Join(", ", score.Assignment.SpeakingSeconds.Select(s => DiarisationBench.F(s, 1)))}]";

    [Theory]
    [MemberData(nameof(LongFiles))]
    public void ATenMinuteFileFindsItsSpeakerCountWithoutTheHint(string id)
    {
        PipelineScore score = PipelineDiarisation.Score(Run(id), new DiarisationOptions());

        // Measured: 6 of 6, 4 of 4 and 5 of 5, attribution 0.87 to 0.93 with word runs as the turns.
        Assert.True(score.EstimatedSpeakers == score.Run.TrueSpeakers && score.Attribution >= 0.82, $"got {Show(score)}; wanted the true count and attribution at least 0.82");
    }

    [Theory]
    [MemberData(nameof(LongFiles))]
    public void ATenMinuteFileFindsItsSpeakerCountWithTheHint(string id)
    {
        PipelineRun run = Run(id);
        PipelineScore score = PipelineDiarisation.Score(run, new DiarisationOptions { SpeakerCountHint = run.TrueSpeakers });

        Assert.True(score.EstimatedSpeakers == run.TrueSpeakers && score.Attribution >= 0.82, $"got {Show(score)}; wanted the true count and attribution at least 0.82");
    }

    [Theory]
    [MemberData(nameof(ConstructedClips))]
    public void AConstructedClipFindsItsSpeakerCountWithoutTheHint(string id)
    {
        PipelineScore score = PipelineDiarisation.Score(Run(id), new DiarisationOptions());

        // Measured: every clip exact, attribution 0.86 to 0.94.
        Assert.True(score.EstimatedSpeakers == score.Run.TrueSpeakers && score.Attribution >= 0.8, $"got {Show(score)}; wanted the true count and attribution at least 0.8");
    }

    [Theory]
    [MemberData(nameof(MeetingClips))]
    public void AMeetingClipIsWithinOneSpeakerOfItsCount(string id)
    {
        PipelineScore score = PipelineDiarisation.Score(Run(id), new DiarisationOptions());

        // Measured: 4, 3 and 3 speakers for 4.
        Assert.True(Math.Abs(score.EstimatedSpeakers - score.Run.TrueSpeakers) <= 1, $"got {Show(score)}; wanted the count within one of the truth");
    }

    [Fact]
    public void TheHintOverridesTheSpeakerFloor()
    {
        // A floor of 20 % of the speaking time folds the quieter real speakers of en-10min; the hint
        // still gives the count asked for.
        PipelineRun run = Run("en-10min");
        PipelineScore folded = PipelineDiarisation.Score(run, new DiarisationOptions { SpeakerFloorShare = 0.2 });
        PipelineScore hinted = PipelineDiarisation.Score(run, new DiarisationOptions { SpeakerFloorShare = 0.2, SpeakerCountHint = run.TrueSpeakers });

        Assert.True(folded.EstimatedSpeakers < run.TrueSpeakers, $"got {Show(folded)}; wanted a floor of 20 % to fold some speakers");
        Assert.True(hinted.EstimatedSpeakers == run.TrueSpeakers, $"got {Show(hinted)}; wanted the hinted count");
    }
}
