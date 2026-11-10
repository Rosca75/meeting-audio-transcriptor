// One word as a recogniser returns it: its text, where it sits in time, and how sure the model was.
namespace MeetingAudioTranscriptor.Engine.Recognition;

/// <summary>A recognised word with its position in seconds.</summary>
/// <param name="Text">The word as the model wrote it, with its casing and attached punctuation.</param>
/// <param name="Start">Where the word starts, in seconds.</param>
/// <param name="End">Where the word ends, in seconds.</param>
/// <param name="Confidence">The model's probability for the word, between 0 and 1.</param>
public readonly record struct RecognizedWord(string Text, double Start, double End, float Confidence);
