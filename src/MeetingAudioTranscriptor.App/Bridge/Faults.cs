// Tells a programming fault, whose message is for a developer, from a failure whose message was
// written for the user.
using System;
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Classifies exceptions for the sentences the page shows.</summary>
internal static class Faults
{
    /// <summary>True for the exception types that mean a bug rather than a condition the user can fix.</summary>
    public static bool IsProgrammingFault(Exception failure)
    {
        return failure is NullReferenceException or InvalidCastException or IndexOutOfRangeException
            or KeyNotFoundException or ArgumentNullException;
    }

    /// <summary>The sentence for a failure: generic for a fault, the exception's own message otherwise.</summary>
    public static string Sentence(string what, Exception failure)
    {
        return IsProgrammingFault(failure)
            ? $"{what} failed unexpectedly ({failure.GetType().Name}). Try again, and if it keeps failing restart the application."
            : failure.Message;
    }
}
