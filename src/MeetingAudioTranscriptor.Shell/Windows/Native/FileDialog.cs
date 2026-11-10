// The system's common item dialog (IFileOpenDialog) for choosing one audio file or one folder,
// called through its COM virtual table with function pointers, so no desktop framework and no
// run-time interop marshalling is needed.
using System;
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Shell.Windows.Native;

/// <summary>
/// Opens the Windows open dialog for a file or a folder and answers the chosen path, or null when the
/// user cancelled. Every call runs on the interface thread, which is a single-threaded COM apartment.
/// </summary>
/// <remarks>
/// <para>
/// The calls go through the object's virtual table by slot. The slots are counted in ShObjIdl_core.h:
/// IUnknown holds 0 to 2 (QueryInterface, AddRef, Release); IModalWindow adds Show (3); IFileDialog
/// adds SetFileTypes (4), SetFileTypeIndex (5), GetFileTypeIndex (6), Advise (7), Unadvise (8),
/// SetOptions (9), GetOptions (10), SetDefaultFolder (11), SetFolder (12), GetFolder (13),
/// GetCurrentSelection (14), SetFileName (15), GetFileName (16), SetTitle (17), SetOkButtonLabel (18),
/// SetFileNameLabel (19), GetResult (20), and further slots this file does not use. IShellItem holds
/// BindToHandler (3), GetParent (4) and GetDisplayName (5).
/// </para>
/// <para>
/// Every method is declared STDMETHODCALLTYPE (stdcall), which is the platform default on x64 and
/// arm64 and is stated anyway. Strings are UTF-16, as every W interface takes them.
/// </para>
/// </remarks>
internal static unsafe class FileDialog
{
    private const int SlotRelease = 2;
    private const int SlotShow = 3;
    private const int SlotSetFileTypes = 4;
    private const int SlotSetFileTypeIndex = 5;
    private const int SlotSetOptions = 9;
    private const int SlotGetOptions = 10;
    private const int SlotSetFolder = 12;
    private const int SlotSetTitle = 17;
    private const int SlotGetResult = 20;
    private const int SlotGetDisplayName = 5;

    // ShObjIdl_core.h (FILEOPENDIALOGOPTIONS).
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint FOS_FILEMUSTEXIST = 0x00001000;

    // ShObjIdl_core.h (SIGDN): the display name that is the file system path.
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    // HRESULT_FROM_WIN32(ERROR_CANCELLED): the answer Show gives when the user cancels.
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    private static readonly Guid FileOpenDialogClass = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    private static readonly Guid FileOpenDialogInterface = new("d57c7288-d4ad-4768-be02-9d969532d960");
    private static readonly Guid ShellItemInterface = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>COMDLG_FILTERSPEC (ShTypes.h): a display name and a semicolon-separated pattern list.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct COMDLG_FILTERSPEC
    {
        public char* pszName;
        public char* pszSpec;
    }

    /// <summary>The open dialog filtered to the three recording extensions; null when cancelled.</summary>
    public static string? PickAudioFile(nint owner) =>
        Show(owner, "Choose a meeting recording", FOS_FILEMUSTEXIST, initialFolder: null, withAudioFilter: true);

    /// <summary>The folder picker, starting at the given folder when it exists; null when cancelled.</summary>
    public static string? PickFolder(nint owner, string? initialFolder) =>
        Show(owner, "Choose a folder", FOS_PICKFOLDERS, initialFolder, withAudioFilter: false);

    private static string? Show(nint owner, string title, uint extraOptions, string? initialFolder, bool withAudioFilter)
    {
        Check(
            Win32.CoCreateInstance(in FileOpenDialogClass, 0, Win32.CLSCTX_INPROC_SERVER, in FileOpenDialogInterface, out nint dialog),
            "the file dialog could not be created");
        try
        {
            uint options;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(dialog, SlotGetOptions))(dialog, &options), "the file dialog's options could not be read");
            options |= FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | extraOptions;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(dialog, SlotSetOptions))(dialog, options), "the file dialog's options could not be set");

            fixed (char* titleText = title)
            {
                Check(((delegate* unmanaged[Stdcall]<nint, char*, int>)Slot(dialog, SlotSetTitle))(dialog, titleText), "the file dialog's title could not be set");
            }

            if (withAudioFilter)
            {
                fixed (char* name = "Meeting recordings (*.m4a, *.mp4, *.wav)")
                {
                    fixed (char* spec = "*.m4a;*.mp4;*.wav")
                    {
                        // The dialog copies the strings during the call, so pinned managed strings suffice.
                        COMDLG_FILTERSPEC filter = new() { pszName = name, pszSpec = spec };
                        Check(((delegate* unmanaged[Stdcall]<nint, uint, COMDLG_FILTERSPEC*, int>)Slot(dialog, SlotSetFileTypes))(dialog, 1, &filter), "the file dialog's filter could not be set");
                    }
                }

                // The type index is one-based.
                Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(dialog, SlotSetFileTypeIndex))(dialog, 1), "the file dialog's filter could not be selected");
            }

            if (!string.IsNullOrEmpty(initialFolder)
                && Win32.SHCreateItemFromParsingName(initialFolder, 0, in ShellItemInterface, out nint folder) >= 0)
            {
                // A folder that cannot be opened is not an error: the dialog starts where it last was.
                _ = ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(dialog, SlotSetFolder))(dialog, folder);
                Release(folder);
            }

            int shown = ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(dialog, SlotShow))(dialog, owner);
            if (shown == ErrorCancelled)
            {
                return null;
            }

            Check(shown, "the file dialog could not be opened");

            nint item;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(dialog, SlotGetResult))(dialog, &item), "the chosen item could not be read");
            try
            {
                char* path;
                Check(((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)Slot(item, SlotGetDisplayName))(item, SIGDN_FILESYSPATH, &path), "the chosen path could not be read");
                try
                {
                    return new string(path);
                }
                finally
                {
                    // The name is allocated by the shell with the COM task allocator.
                    Marshal.FreeCoTaskMem((nint)path);
                }
            }
            finally
            {
                Release(item);
            }
        }
        finally
        {
            Release(dialog);
        }
    }

    /// <summary>The function pointer in one slot of a COM object's virtual table.</summary>
    private static nint Slot(nint comObject, int slot) => (*(nint**)comObject)[slot];

    private static void Release(nint comObject) =>
        _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(comObject, SlotRelease))(comObject);

    private static void Check(int result, string what)
    {
        if (result < 0)
        {
            throw new InvalidOperationException(
                $"{what} (HRESULT 0x{result:X8}); try again, and if it keeps failing restart the application");
        }
    }
}
