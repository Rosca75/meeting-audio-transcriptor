// Tests the wording and the kind of every decoder failure: ffmpeg's stderr phrases, the Media Foundation
// HRESULTs and output types, and the content sniff that routes a file to a decoder.
using System;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

[Trait("Category", "errors")]
public sealed class DecodeErrorTests
{
    [Theory]
    [InlineData("[in#0 @ 0x5558aea97dc0] Error opening input: No such file or directory", AudioDecodeFailure.FileNotFound)]
    [InlineData("C:\\x.m4a: The system cannot find the file specified.", AudioDecodeFailure.FileNotFound)]
    [InlineData("/root/x.m4a: Permission denied", AudioDecodeFailure.AccessDenied)]
    [InlineData("Stream map '0:a:0' matches no streams.", AudioDecodeFailure.NoAudioStream)]
    [InlineData("Output file #0 does not contain any stream", AudioDecodeFailure.NoAudioStream)]
    [InlineData("Error opening input files: Invalid data found when processing input", AudioDecodeFailure.CorruptOrUnsupportedData)]
    [InlineData("[mov,mp4,m4a,3gp,3g2,mj2 @ 0x5612] moov atom not found", AudioDecodeFailure.CorruptOrUnsupportedData)]
    [InlineData("something nobody has seen before", AudioDecodeFailure.DecoderFailed)]
    public void FfmpegPhrasesMapToTheirKind(string line, AudioDecodeFailure expected)
    {
        AudioDecodeFailure kind = FfmpegErrors.ClassifyKind([line]);

        Assert.True(kind == expected, $"got {kind}, wanted {expected} for '{line}'");
    }

    [Fact]
    public void AnFfmpegMessageQuotesTheLastLinesWithoutAddressesAndSaysWhatToDo()
    {
        string[] lines =
        [
            "first line that is dropped",
            "second",
            "[in#0 @ 0x559b72139dc0] Error opening input: Invalid data found when processing input",
            "Error opening input file file:/tmp/bad.m4a.",
            "Error opening input files: Invalid data found when processing input",
        ];

        AudioDecodeException e = FfmpegErrors.Classify("/tmp/bad.m4a", lines, 183);

        Assert.True(e.Failure == AudioDecodeFailure.CorruptOrUnsupportedData, $"got {e.Failure}, wanted corrupt data");
        Assert.True(e.Message.Contains("[in#0] Error opening input: Invalid data", StringComparison.Ordinal), $"got '{e.Message}', wanted ffmpeg's line without its address");
        Assert.True(!e.Message.Contains("first line", StringComparison.Ordinal), $"got '{e.Message}', wanted only the last four lines");
        Assert.True(e.Message.Contains("export the recording again", StringComparison.Ordinal), $"got '{e.Message}', wanted the remedy");
    }

    [Fact]
    public void ASilentFailureNamesTheExitCode()
    {
        AudioDecodeException e = FfmpegErrors.Classify("/tmp/x.m4a", [], 69);

        Assert.True(e.Message.Contains("exited with code 69", StringComparison.Ordinal), $"got '{e.Message}', wanted the exit code named");
    }

    [Fact]
    public void AMissingFfmpegSaysWhereItLookedAndHowToFixIt()
    {
        FfmpegLocator locator = new(_ => null, "/opt/mat", isWindows: false, _ => false);

        AudioDecodeException e = FfmpegErrors.NotFound(locator);

        Assert.True(e.Failure == AudioDecodeFailure.DecoderMissing, $"got {e.Failure}, wanted decoder missing");
        Assert.True(
            e.Message.StartsWith("ffmpeg was not found beside the application nor on the PATH", StringComparison.Ordinal)
            && e.Message.EndsWith("reinstall the package or install ffmpeg", StringComparison.Ordinal),
            $"got '{e.Message}', wanted the documented sentence");
    }

    [Theory]
    [InlineData(unchecked((int)0x80070002), AudioDecodeFailure.FileNotFound, "ERROR_FILE_NOT_FOUND")]
    [InlineData(unchecked((int)0x80070005), AudioDecodeFailure.AccessDenied, "E_ACCESSDENIED")]
    [InlineData(unchecked((int)0xC00D36C4), AudioDecodeFailure.CorruptOrUnsupportedData, "MF_E_UNSUPPORTED_BYTESTREAM_TYPE")]
    [InlineData(unchecked((int)0xC00D36B4), AudioDecodeFailure.CorruptOrUnsupportedData, "MF_E_INVALIDMEDIATYPE")]
    [InlineData(unchecked((int)0xC00D5212), AudioDecodeFailure.CorruptOrUnsupportedData, "MF_E_TOPO_CODEC_NOT_FOUND")]
    [InlineData(unchecked((int)0xC00D36B3), AudioDecodeFailure.NoAudioStream, "MF_E_INVALIDSTREAMNUMBER")]
    [InlineData(unchecked((int)0x8000FFFF), AudioDecodeFailure.DecoderFailed, "an unexpected error")]
    public void HresultsMapToTheirKindNameAndHexCode(int hresult, AudioDecodeFailure expected, string name)
    {
        AudioDecodeException e = MediaFoundationErrors.ToException(hresult, "C:\\m.m4a", "open");
        string hex = "0x" + hresult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(e.Failure == expected, $"got {e.Failure}, wanted {expected} for {hex}");
        Assert.True(e.Message.Contains(name, StringComparison.Ordinal) && e.Message.Contains(hex, StringComparison.Ordinal), $"got '{e.Message}', wanted {name} and {hex}");
        Assert.True(e.Message.Contains("; ", StringComparison.Ordinal), $"got '{e.Message}', wanted a remedy after the failure");
    }

    [Theory]
    [InlineData((int)MediaFoundationSubtype.Float, 32, (int)PcmSampleFormat.Float32)]
    [InlineData((int)MediaFoundationSubtype.Pcm, 16, (int)PcmSampleFormat.Int16)]
    [InlineData((int)MediaFoundationSubtype.Pcm, 24, (int)PcmSampleFormat.Int24)]
    [InlineData((int)MediaFoundationSubtype.Pcm, 32, (int)PcmSampleFormat.Int32)]
    public void MediaFoundationOutputTypesMapToConverterFormats(int subtype, int bits, int expected)
    {
        PcmSampleFormat format = MediaFoundationErrors.ToPcmFormat((MediaFoundationSubtype)subtype, bits, "m.m4a");

        Assert.True(format == (PcmSampleFormat)expected, $"got {format}, wanted {(PcmSampleFormat)expected} for {(MediaFoundationSubtype)subtype} {bits} bits");
    }

    [Theory]
    [InlineData((int)MediaFoundationSubtype.Pcm, 8)]
    [InlineData((int)MediaFoundationSubtype.Float, 64)]
    [InlineData((int)MediaFoundationSubtype.Other, 16)]
    public void AnUnreadableOutputTypeIsAnActionableFailure(int subtype, int bits)
    {
        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => MediaFoundationErrors.ToPcmFormat((MediaFoundationSubtype)subtype, bits, "m.m4a"));

        Assert.True(e.Message.Contains("wanted 32-bit float or 16, 24 or 32-bit PCM", StringComparison.Ordinal), $"got '{e.Message}', wanted what is read named");
    }

    [Fact]
    public void ADurationIsConvertedFromHundredNanosecondUnits()
    {
        double? seconds = MediaFoundationErrors.DurationSeconds(36_000_000_000UL);

        Assert.True(seconds == 3600d && MediaFoundationErrors.DurationSeconds(0) is null, $"got {seconds}, wanted 3600 s, and null for zero");
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(44100, 0)]
    [InlineData(44100, 65)]
    public void AnImpossibleLayoutIsRefusedBeforeAConverterIsBuilt(int rate, int channels)
    {
        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => MediaFoundationErrors.ValidateLayout(rate, channels, "m.m4a"));

        Assert.True(e.Failure == AudioDecodeFailure.CorruptOrUnsupportedData, $"got {e.Failure}, wanted corrupt data for {rate} Hz {channels} channels");
    }

    [Theory]
    [InlineData("RIFF\x24\0\0\0WAVEfmt ", (int)ContainerKind.Wav)]
    [InlineData("RF64\xff\xff\xff\xffWAVEds64", (int)ContainerKind.Wav)]
    [InlineData("BW64\xff\xff\xff\xffWAVEds64", (int)ContainerKind.Wav)]
    [InlineData("\0\0\0\x20" + "ftypM4A \0\0\0\0", (int)ContainerKind.IsoMedia)]
    [InlineData("\0\0\0\x18" + "ftypisom\0\0\x02\0", (int)ContainerKind.IsoMedia)]
    [InlineData("RIFF\x24\0\0\0AVI LIST", (int)ContainerKind.Unknown)]
    [InlineData("ID3\x04\0\0\0\0\0\0\0\0", (int)ContainerKind.Unknown)]
    [InlineData("RIFF", (int)ContainerKind.Unknown)]
    public void TheSniffReadsTheContainerFromTheFirstTwelveBytes(string head, int expected)
    {
        byte[] bytes = System.Text.Encoding.Latin1.GetBytes(head);

        ContainerKind kind = ContainerSniffer.Sniff(bytes);

        Assert.True(kind == (ContainerKind)expected, $"got {kind}, wanted {(ContainerKind)expected} for {Convert.ToHexString(bytes)}");
    }
}
