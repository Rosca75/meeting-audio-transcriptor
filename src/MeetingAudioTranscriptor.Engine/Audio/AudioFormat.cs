// The sample rate every model of the engine reads, in one place.
namespace MeetingAudioTranscriptor.Engine.Audio;

/// <summary>The audio format the recognition, voice-activity and speaker models all expect.</summary>
public static class AudioFormat
{
    /// <summary>Samples per second after resampling: every shipped model is trained on 16 kHz mono.</summary>
    public const int ModelSampleRate = 16000;
}
