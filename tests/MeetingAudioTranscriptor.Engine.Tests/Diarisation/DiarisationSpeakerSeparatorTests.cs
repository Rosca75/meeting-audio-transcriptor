// Tests of the pipeline's speaker separator over the diarisation stages: numbering by first word, one speaker without windows, and one finish per run.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Diarisation;

[Trait("Category", "transcript")]
public sealed class DiarisationSpeakerSeparatorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void SpeakersAreNumberedByTheirFirstWordAndKeepTheirTime()
    {
        // The timeline's speaker 1 spoke first but said no word; speaker 2 speaks the first word.
        SpeakerAssignment assignment = DiarisationCollector.Renumber([2, 2, 0, 2, 0], [5.0, 1.0, 7.0]);

        Assert.True(assignment.SpeakerOfWord.SequenceEqual([0, 0, 1, 0, 1]), $"got {string.Join(",", assignment.SpeakerOfWord)}, wanted 0,0,1,0,1");
        Assert.True(assignment.SpeakingSeconds.SequenceEqual([7.0, 5.0]), $"got {string.Join(",", assignment.SpeakingSeconds)}, wanted the time of the two speakers with words");
    }

    [Fact]
    public void WordsOfATimelineWithoutTurnsMakeOneSpeaker()
    {
        SpeakerAssignment assignment = DiarisationCollector.Renumber([-1, -1, -1], []);

        Assert.True(assignment.SpeakerOfWord.All(speaker => speaker == 0) && assignment.SpeakingSeconds.Count == 1, $"got {string.Join(",", assignment.SpeakerOfWord)}");
    }

    [Fact]
    public void OneVoiceGivesOneSpeakerForEveryWordAndAFinishOnlyOnce()
    {
        DiarisationSpeakerSeparator separator = new(new SpeakerEmbedder(new ConstantEmbeddings()));
        float[] block = new float[16000 * 10];
        for (int i = 0; i < block.Length; i++)
        {
            block[i] = 0.3f * MathF.Sin(i * 0.05f);
        }

        // Two workers hand in two blocks, the later one first.
        separator.AddBlock(block, 160000, [(0, 160000)], Token);
        separator.AddBlock(block, 0, [(16000, 144000)], Token);
        RecognizedWord[] words = [new("Hello", 1.5, 1.9, 0.9f), new("there.", 2.0, 2.4, 0.9f), new("Again", 12.0, 12.5, 0.9f)];
        SpeakerAssignment assignment = separator.Finish(words, null, Token);

        Assert.True(separator.WindowCount > 4, $"got {separator.WindowCount} windows, wanted the speech of both blocks embedded");
        Assert.True(assignment.SpeakerOfWord.SequenceEqual([0, 0, 0]) && assignment.SpeakingSeconds.Count == 1, $"got {string.Join(",", assignment.SpeakerOfWord)}");
        Assert.Throws<InvalidOperationException>(() => separator.Finish(words, null, Token));
    }

    /// <summary>A speaker model that hears the same voice in every window.</summary>
    private sealed class ConstantEmbeddings : ITensorRunner
    {
        public TensorValue[] Run(ReadOnlySpan<TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation)
        {
            int batch = inputs[0].Shape[0];
            float[] values = new float[batch * 4];
            for (int b = 0; b < batch; b++)
            {
                values[b * 4] = 1;
            }

            return [TensorValue.OfFloats(SpeakerEmbedder.OutputName, [batch, 4], values)];
        }

        public void Dispose()
        {
        }
    }
}
