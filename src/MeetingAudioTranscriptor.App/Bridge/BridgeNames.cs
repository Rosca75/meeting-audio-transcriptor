// The names that cross the bridge as constants, so a typo is a compile error and the page's
// vocabulary is listed in one place.
namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>The method names the page may call.</summary>
/// <remarks>A method outside this list is not reachable from the page; the dispatcher rejects it by name.</remarks>
public static class BridgeMethods
{
    /// <summary>Version, supported extensions and the models check.</summary>
    public const string AppInfo = "app.info";

    /// <summary>Opens the native file dialog.</summary>
    public const string FilePick = "file.pick";

    /// <summary>Tells the host which file names the page saw dropped.</summary>
    public const string FilesDropped = "files.dropped";

    /// <summary>Opens the native folder dialog.</summary>
    public const string FolderPick = "folder.pick";

    /// <summary>Shows a folder in the system file manager.</summary>
    public const string FolderOpen = "folder.open";

    /// <summary>Starts a transcription.</summary>
    public const string RunStart = "run.start";

    /// <summary>Cancels the running transcription, if any.</summary>
    public const string RunCancel = "run.cancel";
}

/// <summary>The event names the host pushes to the page.</summary>
public static class BridgeEvents
{
    /// <summary>The stage and fraction of the running transcription.</summary>
    public const string RunProgress = "run:progress";

    /// <summary>The one terminal event of a run.</summary>
    public const string RunFinished = "run:finished";

    /// <summary>A file was chosen by a drop.</summary>
    public const string FileSelected = "file:selected";

    /// <summary>A file was refused.</summary>
    public const string FileRefused = "file:refused";

    /// <summary>A host failure with no waiting call.</summary>
    public const string HostError = "host:error";
}

/// <summary>The codes carried by the <c>host:error</c> event.</summary>
public static class HostErrorCodes
{
    /// <summary>A message from the page could not be read as a call or a drop.</summary>
    public const string MessageUnreadable = "message_unreadable";

    /// <summary>The bridge itself hit a programming fault.</summary>
    public const string InternalError = "internal_error";

    /// <summary>A drop message could not be turned into a selection.</summary>
    public const string DropFailed = "drop_failed";
}
