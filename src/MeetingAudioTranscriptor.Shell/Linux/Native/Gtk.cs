// The GTK 3 calls the Linux host makes: start-up, the top-level window, the main loop, the native
// file choosers and the URIs of a drop.
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Shell.Linux.Native;

/// <summary>The GTK 3 surface the Linux host touches, and only that (gtk/gtk.h).</summary>
internal static unsafe partial class Gtk
{
    internal const string Library = "libgtk-3.so.0";

    /// <summary>GtkWindowType: an ordinary top-level window.</summary>
    internal const int GTK_WINDOW_TOPLEVEL = 0;

    /// <summary>GtkFileChooserAction: open one existing file.</summary>
    internal const int GTK_FILE_CHOOSER_ACTION_OPEN = 0;

    /// <summary>GtkFileChooserAction: choose a folder.</summary>
    internal const int GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER = 2;

    /// <summary>GtkResponseType: the user accepted the dialog.</summary>
    internal const int GTK_RESPONSE_ACCEPT = -3;

    [LibraryImport(Library)]
    internal static partial int gtk_init_check(nint argc, nint argv);

    [LibraryImport(Library)]
    internal static partial void gtk_main();

    [LibraryImport(Library)]
    internal static partial void gtk_main_quit();

    [LibraryImport(Library)]
    internal static partial nint gtk_window_new(int type);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Library)]
    internal static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Library)]
    internal static partial void gtk_widget_set_size_request(nint widget, int width, int height);

    [LibraryImport(Library)]
    internal static partial void gtk_container_add(nint container, nint widget);

    [LibraryImport(Library)]
    internal static partial void gtk_widget_show_all(nint widget);

    [LibraryImport(Library)]
    internal static partial void gtk_widget_grab_focus(nint widget);

    [LibraryImport(Library)]
    internal static partial void gtk_widget_destroy(nint widget);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint gtk_file_chooser_native_new(string title, nint parent, int action, string? acceptLabel, string? cancelLabel);

    [LibraryImport(Library)]
    internal static partial int gtk_native_dialog_run(nint dialog);

    [LibraryImport(Library)]
    internal static partial nint gtk_file_chooser_get_filename(nint chooser);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gtk_file_chooser_set_current_folder(nint chooser, string folder);

    [LibraryImport(Library)]
    internal static partial void gtk_file_chooser_set_local_only(nint chooser, int localOnly);

    [LibraryImport(Library)]
    internal static partial void gtk_file_chooser_add_filter(nint chooser, nint filter);

    [LibraryImport(Library)]
    internal static partial nint gtk_file_filter_new();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_file_filter_set_name(nint filter, string name);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_file_filter_add_pattern(nint filter, string pattern);

    /// <summary>The URIs a selection holds, as a NULL-terminated array freed with g_strfreev; NULL when it holds none.</summary>
    [LibraryImport(Library)]
    internal static partial nint gtk_selection_data_get_uris(nint selection);
}
