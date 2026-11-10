// The only seam between the bridge and a window: what the bridge needs from the desktop host.
using System.Threading.Tasks;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>What the bridge asks of the window host.</summary>
/// <remarks>
/// The bridge names no window, so a test drives the whole bound surface with a fake of this interface.
/// Each host implements it once: the Windows host over WebView2, the Linux host over WebKitGTK.
/// </remarks>
public interface IHostServices
{
    /// <summary>Posts one JSON envelope to the page. Callable from any thread; must not throw.</summary>
    void Emit(string json);

    /// <summary>Opens the native open-file dialog filtered to .m4a, .mp4 and .wav; null when cancelled.</summary>
    Task<string?> PickAudioFileAsync();

    /// <summary>Opens the native folder dialog, starting at the folder when given; null when cancelled.</summary>
    Task<string?> PickFolderAsync(string? initialFolder);

    /// <summary>Shows the folder in the system file manager.</summary>
    void OpenFolder(string folderPath);
}
