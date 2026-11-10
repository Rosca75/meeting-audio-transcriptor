// Which text of a segment a text writer prints: the cleaned one or the recogniser's own.
namespace MeetingAudioTranscriptor.Engine.Writers;

/// <summary>Which text of each segment a VTT or TXT file carries.</summary>
public enum TranscriptVariant
{
    /// <summary>The cleaned text; segments cleaning removed are left out.</summary>
    Cleaned,

    /// <summary>The text as the recogniser wrote it, for every segment.</summary>
    Raw,
}
