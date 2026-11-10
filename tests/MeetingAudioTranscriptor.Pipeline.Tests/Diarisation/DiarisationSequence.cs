// The test collection every diarisation class joins, so their model runs and timings never overlap each other.
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

/// <summary>Runs the diarisation classes one after another.</summary>
[CollectionDefinition(Name)]
public sealed class DiarisationSequence
{
    /// <summary>The collection's name.</summary>
    public const string Name = "diarisation";
}
