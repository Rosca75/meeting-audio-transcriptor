// One embedding window: a stretch of speech and the voice print the model computed for it.
namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>A window of speech and its L2-normalised speaker embedding.</summary>
/// <param name="StartSample">The first sample of the window, counted from the start of the recording.</param>
/// <param name="EndSample">One past the last sample of the window.</param>
/// <param name="Embedding">The unit-length embedding (512 floats for CAM++).</param>
public sealed record SpeakerWindow(long StartSample, long EndSample, float[] Embedding);
