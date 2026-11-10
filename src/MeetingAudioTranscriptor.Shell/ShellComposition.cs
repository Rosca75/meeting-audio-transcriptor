// The composition root of the desktop executable: which transcriber the bridge drives, and the
// bridge itself over the host's window services.
using System;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.App.Bridge;

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

    /// <summary>
    /// The transcriber a normal start uses, the single place the real service is chosen: the
    /// transcription service over the catalog's models folder (<c>MAT_MODELS_DIR</c> when set, else
    /// <c>models</c> beside the executable). The service loads the models on the first run and keeps
    /// them for later ones; the process exit releases them.
    /// </summary>
    public static ITranscriber CreateTranscriber() => new ServiceTranscriber();
}
