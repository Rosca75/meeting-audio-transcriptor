// The composition root of the desktop executable: which transcriber the bridge drives, and the
// bridge itself over the host's window services.
using System;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.App.Bridge;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>Builds the bridge a host attaches to its page.</summary>
internal static class ShellComposition
{
    /// <summary>
    /// The bridge over the given window services: simulated runs when <c>MAT_UI_DEMO=1</c>, so the
    /// window can be shown without models, and the real transcriber otherwise.
    /// </summary>
    public static Bridge CreateBridge(IHostServices host)
    {
        ArgumentNullException.ThrowIfNull(host);
        ITranscriber transcriber = DemoMode.CreateIfRequested() ?? CreateTranscriber();
        return new Bridge(host, transcriber, AppVersion.Current);
    }

    /// <summary>The transcriber a normal start uses; the single place the real service is chosen.</summary>
    public static ITranscriber CreateTranscriber()
    {
        // replaced when the transcription service is wired
        return new NotConnectedTranscriber();
    }

    /// <summary>
    /// Stands in for the transcription service until it is wired: it reports the models as not ready
    /// with a sentence saying so, which the page shows while keeping Start disabled.
    /// </summary>
    private sealed class NotConnectedTranscriber : ITranscriber
    {
        private const string Message =
            "Transcription is not available in this build yet. Use meeting-audio-transcriptor-cli, or start the window with MAT_UI_DEMO=1 to try the interface.";

        public ModelCheckResult CheckModels() => new() { Ready = false, Message = Message };

        public Task<TranscribeOutcome> RunAsync(TranscribeRequest request, IProgress<TranscribeProgress> progress, CancellationToken cancellation) =>
            Task.FromResult(new TranscribeOutcome { Kind = TranscribeOutcomeKinds.Failed, Message = Message });
    }
}
