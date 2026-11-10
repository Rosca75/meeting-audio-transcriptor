// The window services of the Linux host: envelopes evaluated into the page, the GTK native file
// choosers, and the folder shown in the desktop's file manager.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Shell.Linux.Native;

namespace MeetingAudioTranscriptor.Shell.Linux;

/// <summary>
/// <see cref="HostServicesBase"/> over GTK and WebKitGTK. Every call that touches a widget runs on
/// the GTK main thread through <see cref="GtkMainThread"/>.
/// </summary>
internal sealed unsafe class LinuxHostServices : HostServicesBase
{
    private readonly GtkMainThread _main;

    public LinuxHostServices(GtkMainThread main)
    {
        ArgumentNullException.ThrowIfNull(main);
        _main = main;
    }

    /// <summary>The top-level window the dialogs are attached to, set once it exists.</summary>
    public nint Window { get; set; }

    /// <summary>The web view envelopes are evaluated in, set once it exists.</summary>
    public nint WebView { get; set; }

    /// <inheritdoc />
    public override Task<string?> PickAudioFileAsync() => OnUiThread(() => Choose(
        "Choose a meeting recording",
        Gtk.GTK_FILE_CHOOSER_ACTION_OPEN,
        "_Open",
        initialFolder: null,
        filter: true));

    /// <inheritdoc />
    public override Task<string?> PickFolderAsync(string? initialFolder) => OnUiThread(() => Choose(
        "Choose a folder",
        Gtk.GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER,
        "_Select",
        initialFolder,
        filter: false));

    /// <summary>
    /// Opens the folder with the desktop's default handler for a <c>file://</c> URI, which is the file
    /// manager; when GIO finds no handler, <c>xdg-open</c> is asked instead. Nothing is reported back,
    /// because the bridge contract has the call return nothing.
    /// </summary>
    public override void OpenFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        PostToUiThread(() =>
        {
            GLib.GError* error = null;
            string? uri = GLib.TakeUtf8(GLib.g_filename_to_uri(folderPath, null, &error));
            if (uri is null)
            {
                Debug.WriteLine("the folder could not be turned into a URI: " + GLib.TakeErrorMessage(error));
                return;
            }

            error = null;
            if (GLib.g_app_info_launch_default_for_uri(uri, 0, &error) != 0)
            {
                return;
            }

            Debug.WriteLine("the file manager could not be opened through GIO: " + GLib.TakeErrorMessage(error));
            try
            {
                ProcessStartInfo start = new("xdg-open") { UseShellExecute = false };
                start.ArgumentList.Add(folderPath);
                using Process? process = Process.Start(start);
            }
            catch (Win32Exception failure)
            {
                Debug.WriteLine("xdg-open could not be started: " + failure.Message);
            }
        });
    }

    /// <inheritdoc />
    protected override void PostToUiThread(Action work) => _main.Post(work);

    /// <summary>
    /// Evaluates <c>window.__hostReceive("&lt;envelope&gt;")</c> in the page, the call the page's
    /// bridge module defines; before that module has run, the envelope is dropped by the page itself.
    /// </summary>
    protected override void DeliverToPage(string json)
    {
        if (WebView == 0)
        {
            return;
        }

        string script = "typeof window.__hostReceive === \"function\" && window.__hostReceive(" + JsLiteral.Quote(json) + ");";
        WebKit.webkit_web_view_evaluate_javascript(WebView, script, -1, null, null, 0, 0, 0);
    }

    /// <summary>
    /// Runs one native file chooser modally on the main thread and answers the chosen path, or null
    /// when the user cancelled. GtkFileChooserNative uses the desktop portal when one is running and
    /// the GTK dialog otherwise.
    /// </summary>
    private string? Choose(string title, int action, string acceptLabel, string? initialFolder, bool filter)
    {
        nint dialog = Gtk.gtk_file_chooser_native_new(title, Window, action, acceptLabel, "_Cancel");
        if (dialog == 0)
        {
            throw new InvalidOperationException("the file chooser could not be created; try again, and if it keeps failing restart the application");
        }

        try
        {
            Gtk.gtk_file_chooser_set_local_only(dialog, 1);
            if (filter)
            {
                // The chooser takes ownership of the filter, which starts with a floating reference.
                nint audio = Gtk.gtk_file_filter_new();
                Gtk.gtk_file_filter_set_name(audio, "Meeting recordings (.m4a, .mp4, .wav)");
                foreach (string extension in new[] { "m4a", "mp4", "wav" })
                {
                    // Patterns are case-sensitive, so the two common spellings are both listed.
                    Gtk.gtk_file_filter_add_pattern(audio, "*." + extension);
                    Gtk.gtk_file_filter_add_pattern(audio, "*." + extension.ToUpperInvariant());
                }

                Gtk.gtk_file_chooser_add_filter(dialog, audio);
            }

            if (!string.IsNullOrEmpty(initialFolder))
            {
                Gtk.gtk_file_chooser_set_current_folder(dialog, initialFolder);
            }

            if (Gtk.gtk_native_dialog_run(dialog) != Gtk.GTK_RESPONSE_ACCEPT)
            {
                return null;
            }

            return GLib.TakeUtf8(Gtk.gtk_file_chooser_get_filename(dialog));
        }
        finally
        {
            GLib.g_object_unref(dialog);
        }
    }
}
