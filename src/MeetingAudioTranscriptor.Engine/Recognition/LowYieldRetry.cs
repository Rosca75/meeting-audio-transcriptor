// A recogniser decorator that reads again, on its own, a long speech region in which the chunk's recognition found almost no words.
using System;
using System.Collections.Generic;
using System.Threading;

namespace MeetingAudioTranscriptor.Engine.Recognition;

/// <summary>The settings of <see cref="LowYieldRetry"/>.</summary>
public sealed record LowYieldRetryOptions
{
    /// <summary>A region shorter than this, in seconds, is never read again: a short region may hold one word.</summary>
    public double MinRegionSeconds { get; init; } = 1.5;

    /// <summary>A region yielding fewer words per second of its length than this is read again.</summary>
    public double MinWordsPerSecond { get; init; } = 0.5;

    /// <summary>The audio kept on each side of the region when it is read again, in seconds.</summary>
    public double MarginSeconds { get; init; } = 0.2;

    /// <summary>
    /// A region covering at least this fraction of its chunk is not read again, because the second
    /// reading would be the first one over again.
    /// </summary>
    public double MaxChunkFraction { get; init; } = 0.9;

    /// <summary>Throws when a setting is outside its range.</summary>
    internal void Validate()
    {
        if (!(MinRegionSeconds > 0) || !(MinWordsPerSecond > 0) || !(MarginSeconds >= 0) || !(MaxChunkFraction > 0 && MaxChunkFraction <= 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinRegionSeconds),
                $"the retry settings are out of range (shortest region {MinRegionSeconds} s, words per second {MinWordsPerSecond}, margin {MarginSeconds} s, chunk fraction {MaxChunkFraction}); "
                + "use a shortest region and a word rate above zero, a margin of at least zero and a chunk fraction in (0, 1]");
        }
    }
}

/// <summary>Reads again, on its own, each long speech region in which the chunk's reading found too few words.</summary>
/// <remarks>
/// <para>
/// A recogniser reading a chunk where one speaker is much louder than another can drop the quiet
/// speaker's sentences entirely. When the chunk's speech regions are known, a region of at least
/// <see cref="LowYieldRetryOptions.MinRegionSeconds"/> that yielded fewer than
/// <see cref="LowYieldRetryOptions.MinWordsPerSecond"/> words per second is recognised again from
/// its own samples (plus a margin), so a region-aware recogniser levels it on its own. The second
/// reading replaces the words of the first inside the region only when it finds more words.
/// </para>
/// <para>
/// The decorator turns any recogniser into a region-aware one, so the pipeline needs to know
/// nothing about the model. Without regions it is the inner recogniser. The counters are updated
/// atomically; everything else belongs to one call, so it is as thread-safe as the inner recogniser.
/// </para>
/// </remarks>
public sealed class LowYieldRetry : IRegionAwareRecognizer
{
    private readonly ISpeechRecognizer _inner;
    private readonly LowYieldRetryOptions _options;
    private long _chunks;
    private long _retries;
    private long _replaced;

    /// <summary>Wraps a recogniser.</summary>
    /// <param name="inner">The recogniser that reads chunks and regions.</param>
    /// <param name="options">The settings; null takes the defaults.</param>
    public LowYieldRetry(ISpeechRecognizer inner, LowYieldRetryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _options = options ?? new LowYieldRetryOptions();
        _options.Validate();
    }

    /// <inheritdoc />
    public double MaxChunkSeconds => _inner.MaxChunkSeconds;

    /// <summary>The chunks read so far.</summary>
    public long Chunks => Interlocked.Read(ref _chunks);

    /// <summary>The regions read a second time so far, each one extra model run.</summary>
    public long Retries => Interlocked.Read(ref _retries);

    /// <summary>The second readings that replaced the words of the first.</summary>
    public long Replaced => Interlocked.Read(ref _replaced);

    /// <inheritdoc />
    public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation) =>
        Recognize(samples, [], cancellation);

    /// <inheritdoc />
    public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(speechRegions);
        Interlocked.Increment(ref _chunks);
        IReadOnlyList<RecognizedWord> first = ReadWith(samples, speechRegions, cancellation);
        if (speechRegions.Count == 0)
        {
            return first;
        }

        List<RecognizedWord>? words = null;
        double rate = Audio.AudioFormat.ModelSampleRate;
        int margin = (int)Math.Round(_options.MarginSeconds * rate);
        foreach ((int regionStart, int regionEnd) in speechRegions)
        {
            int start = Math.Clamp(regionStart, 0, samples.Length);
            int end = Math.Clamp(regionEnd, start, samples.Length);
            double seconds = (end - start) / rate;
            int sliceStart = Math.Max(0, start - margin);
            int sliceEnd = Math.Min(samples.Length, end + margin);
            if (seconds < _options.MinRegionSeconds || sliceEnd - sliceStart >= _options.MaxChunkFraction * samples.Length)
            {
                continue;
            }

            double from = start / rate;
            double to = end / rate;
            IReadOnlyList<RecognizedWord> current = words ?? first;
            int found = CountInside(current, from, to);
            if (found >= _options.MinWordsPerSecond * seconds)
            {
                continue;
            }

            cancellation.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _retries);
            double offset = sliceStart / rate;
            IReadOnlyList<RecognizedWord> again = ReadWith(samples[sliceStart..sliceEnd], [(start - sliceStart, end - sliceStart)], cancellation);
            List<RecognizedWord> inside = [];
            foreach (RecognizedWord word in again)
            {
                RecognizedWord shifted = word with { Start = word.Start + offset, End = word.End + offset };
                if (IsInside(shifted, from, to))
                {
                    inside.Add(shifted);
                }
            }

            if (inside.Count > found)
            {
                Interlocked.Increment(ref _replaced);
                words = Replace(current, inside, from, to);
            }
        }

        return words ?? first;
    }

    private IReadOnlyList<RecognizedWord> ReadWith(ReadOnlyMemory<float> samples, IReadOnlyList<(int Start, int End)> regions, CancellationToken cancellation) =>
        _inner is IRegionAwareRecognizer aware ? aware.Recognize(samples, regions, cancellation) : _inner.Recognize(samples, cancellation);

    /// <summary>A word belongs to a region when its midpoint lies inside it.</summary>
    private static bool IsInside(RecognizedWord word, double from, double to)
    {
        double middle = (word.Start + word.End) / 2;
        return middle >= from && middle < to;
    }

    private static int CountInside(IReadOnlyList<RecognizedWord> words, double from, double to)
    {
        int count = 0;
        foreach (RecognizedWord word in words)
        {
            if (IsInside(word, from, to))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Swaps the words of a region for the second reading's, keeping time order, and keeps each
    /// word ending no later than the next one starts.
    /// </summary>
    internal static List<RecognizedWord> Replace(IReadOnlyList<RecognizedWord> words, List<RecognizedWord> inside, double from, double to)
    {
        List<RecognizedWord> result = new(words.Count + inside.Count);
        foreach (RecognizedWord word in words)
        {
            if (!IsInside(word, from, to))
            {
                result.Add(word);
            }
        }

        result.AddRange(inside);
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 0; i + 1 < result.Count; i++)
        {
            if (result[i].End > result[i + 1].Start)
            {
                result[i] = result[i] with { End = Math.Max(result[i].Start, result[i + 1].Start) };
            }
        }

        return result;
    }
}
