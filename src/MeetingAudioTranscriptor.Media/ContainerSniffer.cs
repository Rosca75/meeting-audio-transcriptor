// Tells a WAV file from an MP4-family file by its first bytes, so a misnamed file still reaches the right decoder.
using System;

namespace MeetingAudioTranscriptor.Media;

/// <summary>The container families the opener tells apart.</summary>
internal enum ContainerKind
{
    /// <summary>Neither signature matched.</summary>
    Unknown,

    /// <summary><c>RIFF....WAVE</c>, <c>RF64....WAVE</c> or <c>BW64....WAVE</c>.</summary>
    Wav,

    /// <summary>An ISO base media file: <c>....ftyp</c>, which covers .m4a, .mp4 and .mov.</summary>
    IsoMedia,
}

/// <summary>Reads the container family from the first twelve bytes of a file.</summary>
/// <remarks>The content decides and the extension is only a hint, because recordings are renamed by hand and by tools far more often than they are re-encoded.</remarks>
internal static class ContainerSniffer
{
    /// <summary>How many leading bytes the sniff reads.</summary>
    public const int HeaderLength = 12;

    /// <summary>Returns the container family the leading bytes announce.</summary>
    public static ContainerKind Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length < HeaderLength)
        {
            return ContainerKind.Unknown;
        }

        ReadOnlySpan<byte> riffId = head[..4];
        if ((riffId.SequenceEqual("RIFF"u8) || riffId.SequenceEqual("RF64"u8) || riffId.SequenceEqual("BW64"u8)) && head[8..12].SequenceEqual("WAVE"u8))
        {
            return ContainerKind.Wav;
        }

        // An ISO file opens with a box whose 4-byte size is followed by its type; the first box is ftyp.
        if (head[4..8].SequenceEqual("ftyp"u8))
        {
            return ContainerKind.IsoMedia;
        }

        return ContainerKind.Unknown;
    }
}
