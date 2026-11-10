// The stage names the page shows during a run, in the words a person reads; the page displays them as they arrive.
using MeetingAudioTranscriptor.Engine.Pipeline;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>The words of <c>run:progress</c>'s <c>stage</c>, shared by the real and the demo transcriber.</summary>
/// <remarks>
/// The page holds no stage names of its own and shows the host's words unchanged, so the host keeps
/// one short list and maps every pipeline stage onto it: a stage the list lacks takes the nearest
/// entry, which keeps what the user reads the same whichever transcriber runs.
/// </remarks>
public static class BridgeStages
{
    /// <summary>Opening the recording and loading the models.</summary>
    public const string ReadingAudio = "Reading the audio";

    /// <summary>Voice activity; the real pipeline does it together with recognition.</summary>
    public const string FindingSpeech = "Finding speech";

    /// <summary>Recognition.</summary>
    public const string RecognisingWords = "Recognising words";

    /// <summary>Speaker separation.</summary>
    public const string TellingSpeakersApart = "Telling speakers apart";

    /// <summary>Segmentation, cleaning and the files.</summary>
    public const string WritingTranscript = "Writing the transcript";

    /// <summary>Every stage in run order.</summary>
    public static readonly string[] All = [ReadingAudio, FindingSpeech, RecognisingWords, TellingSpeakersApart, WritingTranscript];

    /// <summary>The page's words for a pipeline stage.</summary>
    public static string Of(TranscriptionStage stage) => stage switch
    {
        TranscriptionStage.LoadingModels => ReadingAudio,
        TranscriptionStage.Transcribing => RecognisingWords,
        TranscriptionStage.IdentifyingSpeakers => TellingSpeakersApart,
        _ => WritingTranscript,
    };
}
