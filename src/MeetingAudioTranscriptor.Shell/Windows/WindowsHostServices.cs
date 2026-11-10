// The window services of the Windows host: envelopes posted to the web view, the system open and
// folder dialogs, and the folder shown in File Explorer.
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Shell.Windows.Native;
using Microsoft.Web.WebView2.Core;

namespace MeetingAudioTranscriptor.Shell.Windows;

/// <summary><see cref="HostServicesBase"/> over Win32 and WebView2, every widget call on the interface thread.</summary>
internal sealed class WindowsHostServices : HostServicesBase
{
    private readonly UiThread _ui;

    public WindowsHostServices(UiThread ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
    }

    /// <summary>The window the dialogs are owned by, set once it exists.</summary>
    public MainWindow? Window { get; set; }

    /// <summary>The web view envelopes are posted to, set once it exists.</summary>
    public CoreWebView2? WebView { get; set; }

    private nint Owner => Window?.Handle ?? 0;

    /// <inheritdoc />
    public override Task<string?> PickAudioFileAsync() => OnUiThread(() => FileDialog.PickAudioFile(Owner));

    /// <inheritdoc />
    public override Task<string?> PickFolderAsync(string? initialFolder) => OnUiThread(() => FileDialog.PickFolder(Owner, initialFolder));

    /// <summary>
    /// Opens the folder in File Explorer with the shell's "open" verb. Nothing is reported back,
    /// because the bridge contract has the call return nothing; the bridge checks the folder exists.
    /// </summary>
    public override void OpenFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        PostToUiThread(() =>
        {
            nint result = Win32.ShellExecuteW(Owner, "open", folderPath, null, null, Win32.SW_SHOWNORMAL);
            if (result <= 32)
            {
                Debug.WriteLine($"File Explorer could not open {folderPath} (ShellExecute answered {result})");
            }
        });
    }

    /// <inheritdoc />
    protected override void PostToUiThread(Action work) => _ui.Post(work);

    /// <summary>Posts the envelope as a string; the page reads it from the message event's data.</summary>
    protected override void DeliverToPage(string json) => WebView?.PostWebMessageAsString(json);
}
