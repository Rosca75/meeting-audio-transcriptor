// The one C library call the Linux host makes: setting an environment variable in the native
// environment, which the web view's child processes inherit.
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Shell.Linux.Native;

/// <summary>
/// The C library. .NET keeps its own copy of the environment, so a variable set through
/// <see cref="System.Environment.SetEnvironmentVariable(string, string)"/> never reaches native code;
/// WebKit reads its switches with getenv and passes the environment to its web process, so the
/// variable is set here. The name is the glibc soname, because "libc.so" is a linker script dlopen cannot load.
/// </summary>
internal static partial class LibC
{
    [LibraryImport("libc.so.6", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int setenv(string name, string value, int overwrite);
}
