// The GLib, GObject and GIO calls the Linux host makes: the main loop's idle and timeout sources,
// signal connection, reference counting, byte buffers, memory streams and the default URI handler.
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Shell.Linux.Native;

/// <summary>
/// The GLib family of libraries, as the Linux host uses them. Every declaration is source generated,
/// so a trimmed build carries no marshalling stub discovered at run time, and strings travel as
/// UTF-8, which is what GLib takes for text and, on Linux, for file names.
/// </summary>
/// <remarks>
/// Callbacks are passed as raw function pointers to static methods marked
/// <c>[UnmanagedCallersOnly]</c>, which the garbage collector never moves or collects, so no delegate
/// has to be kept alive by hand.
/// </remarks>
internal static unsafe partial class GLib
{
    private const string GLibLibrary = "libglib-2.0.so.0";
    private const string GObjectLibrary = "libgobject-2.0.so.0";
    private const string GioLibrary = "libgio-2.0.so.0";

    /// <summary>The fundamental type id of a string (G_TYPE_STRING, 16 shifted left by 2).</summary>
    internal const nuint G_TYPE_STRING = 16 << 2;

    /// <summary>A source callback's answer that removes the source after this call.</summary>
    internal const int G_SOURCE_REMOVE = 0;

    /// <summary>
    /// A GValue: the type id and two 64-bit data words, 24 bytes on a 64-bit system
    /// (gobject/gvalue.h). It is only ever initialised, set and unset through GObject's own calls.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GValue
    {
        public nuint GType;
        public ulong Data0;
        public ulong Data1;
    }

    /// <summary>A GError (glib/gerror.h): the domain quark, the code, then the message pointer.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GError
    {
        public uint Domain;
        public int Code;
        public byte* Message;
    }

    [LibraryImport(GLibLibrary)]
    internal static partial uint g_idle_add(delegate* unmanaged<nint, int> function, nint data);

    [LibraryImport(GLibLibrary)]
    internal static partial uint g_timeout_add(uint intervalMilliseconds, delegate* unmanaged<nint, int> function, nint data);

    [LibraryImport(GLibLibrary)]
    internal static partial void g_free(nint memory);

    [LibraryImport(GLibLibrary)]
    internal static partial void g_strfreev(nint strings);

    [LibraryImport(GLibLibrary)]
    internal static partial void g_error_free(GError* error);

    [LibraryImport(GLibLibrary)]
    internal static partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(GLibLibrary)]
    internal static partial void g_bytes_unref(nint bytes);

    [LibraryImport(GLibLibrary, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint g_filename_to_uri(string fileName, string? hostName, GError** error);

    [LibraryImport(GObjectLibrary, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ulong g_signal_connect_data(nint instance, string detailedSignal, nint handler, nint data, nint destroyData, int flags);

    [LibraryImport(GObjectLibrary)]
    internal static partial void g_object_unref(nint instance);

    [LibraryImport(GObjectLibrary)]
    internal static partial nint g_object_new_with_properties(nuint objectType, uint count, byte** names, GValue* values);

    [LibraryImport(GObjectLibrary)]
    internal static partial GValue* g_value_init(GValue* value, nuint type);

    [LibraryImport(GObjectLibrary, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void g_value_set_string(GValue* value, string text);

    [LibraryImport(GObjectLibrary)]
    internal static partial void g_value_unset(GValue* value);

    [LibraryImport(GioLibrary)]
    internal static partial nint g_memory_input_stream_new_from_bytes(nint bytes);

    [LibraryImport(GioLibrary, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int g_app_info_launch_default_for_uri(string uri, nint launchContext, GError** error);

    /// <summary>Connects a handler to a signal; the handler is a static unmanaged function.</summary>
    internal static ulong Connect(nint instance, string signal, nint handler, nint data) =>
        g_signal_connect_data(instance, signal, handler, data, 0, 0);

    /// <summary>Reads a GLib-owned UTF-8 string without freeing it; empty for a null pointer.</summary>
    internal static string ReadUtf8(nint text) => Marshal.PtrToStringUTF8(text) ?? "";

    /// <summary>Reads a newly allocated UTF-8 string and frees it with g_free.</summary>
    internal static string? TakeUtf8(nint text)
    {
        if (text == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(text);
        }
        finally
        {
            g_free(text);
        }
    }

    /// <summary>Reads an error's message and frees the error; the caller passes a non-null error.</summary>
    internal static string TakeErrorMessage(GError* error)
    {
        string message = Marshal.PtrToStringUTF8((nint)error->Message) ?? "unknown error";
        g_error_free(error);
        return message;
    }
}
