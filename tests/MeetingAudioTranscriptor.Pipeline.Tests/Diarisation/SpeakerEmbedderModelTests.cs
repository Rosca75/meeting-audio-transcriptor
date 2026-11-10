// Models-tier tests of the speaker embedder over the real CAM++ graph: the reference recipe's output,
// batching and sharing one model between threads.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

/// <remarks>
/// The graph's contract (read from the model): input feats float [B, T, 80], output embs float [B, 512].
/// The reference values are the first eight of the unit embedding that kaldi-native-fbank features
/// (Hamming window, the recipe's other settings) and ONNX Runtime in Python give for seconds 2.0 to
/// 3.5 of the benchmark clip en-multi-01.
/// </remarks>
[Trait("Category", "diarisation")]
[Trait("Tier", "models")]
[Collection(DiarisationSequence.Name)]
public sealed class SpeakerEmbedderModelTests
{
    private static readonly float[] Reference = [-0.05789f, -0.02088f, -0.06030f, -0.00814f, -0.06293f, -0.11164f, -0.00550f, 0.03900f];

    private static ITensorRunner Open()
    {
        string? model = DiarisationBench.EmbeddingModel();
        Assert.SkipWhen(model is null, $"needs the wespeaker-campplus-lm model in {ModelCatalog.ResolveRoot()}; run: node build/fetch-models.mjs and set MAT_MODELS_DIR");
        return OrtRunnerFactory.Open(model!, new RunnerOptions());
    }

    private static float[] Clip()
    {
        string path = Path.Combine(DiarisationBench.DataDirectory, "clips", "en", "en-multi-01.wav");
        Assert.SkipUnless(File.Exists(path), $"needs {path}; run: node bench/testset/fetch-testset.mjs, or set {DiarisationBench.DataVariable}");
        return DiarisationBench.ReadWav(path);
    }

    [Fact]
    public void TheEmbeddingMatchesTheReferenceRecipe()
    {
        using ITensorRunner runner = Open();
        float[] samples = Clip();

        float[] embedding = new SpeakerEmbedder(runner).Embed(samples.AsSpan(32000, 24000), CancellationToken.None);

        Assert.True(embedding.Length == 512, $"got {embedding.Length} values, wanted 512");
        double norm = Math.Sqrt(embedding.Sum(v => (double)v * v));
        Assert.True(Math.Abs(norm - 1) < 1e-5, $"got norm {norm}, wanted 1");
        for (int i = 0; i < Reference.Length; i++)
        {
            Assert.True(Math.Abs(embedding[i] - Reference[i]) < 2e-3, $"value {i}: got {embedding[i]}, wanted {Reference[i]} within 0.002");
        }
    }

    [Fact]
    public void ABatchGivesTheSameEmbeddingsAsSingleCalls()
    {
        using ITensorRunner runner = Open();
        float[] samples = Clip().AsSpan(0, 16000 * 8).ToArray();
        (int Start, int Length)[] windows = [(0, 24000), (12000, 24000), (24000, 24000), (36000, 24000), (48000, 24000), (60000, 24000), (72000, 24000), (84000, 24000), (96000, 24000), (104000, 24000)];
        float[][] batched = new float[windows.Length][];
        float[][] single = new float[windows.Length][];

        new SpeakerEmbedder(runner, 8).EmbedRegion(samples, windows, batched, CancellationToken.None);
        new SpeakerEmbedder(runner, 1).EmbedRegion(samples, windows, single, CancellationToken.None);

        for (int w = 0; w < windows.Length; w++)
        {
            float dot = VectorDot(batched[w], single[w]);
            Assert.True(dot > 0.9999f, $"window {w}: got cosine {dot} between batched and single, wanted above 0.9999");
        }
    }

    [Fact]
    public void FourThreadsSharingOneEmbedderGetTheSingleThreadedAnswer()
    {
        using ITensorRunner runner = Open();
        float[] samples = Clip();
        SpeakerEmbedder embedder = new(runner, 4);
        float[] expected = embedder.Embed(samples.AsSpan(32000, 24000), CancellationToken.None);

        float[][] got = new float[4][];
        Parallel.For(0, 4, t => got[t] = embedder.Embed(samples.AsSpan(32000, 24000), CancellationToken.None));

        foreach (float[] embedding in got)
        {
            Assert.True(embedding.SequenceEqual(expected), "an embedding computed on another thread differs from the single-threaded one");
        }
    }

    private static float VectorDot(float[] a, float[] b)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}
