// The execution provider the inference project runs models on.
namespace MeetingAudioTranscriptor.Inference;

/// <summary>Names the execution provider, so no caller spells it its own way.</summary>
internal static class InferenceDevice
{
    /// <summary>Models run on the CPU, because the application must work on any machine.</summary>
    public const string Cpu = "CPU";
}
