// Chooses WebKitGTK's software drawing path when the machine has no GPU render node, or when asked to,
// so the window draws on a virtual display and on machines whose graphics stack WebKit cannot use.
using System;
using System.IO;
using MeetingAudioTranscriptor.Shell.Linux.Native;

namespace MeetingAudioTranscriptor.Shell.Linux;

/// <summary>
/// Decides, before GTK starts, whether WebKitGTK must draw in software, and switches it there.
/// </summary>
/// <remarks>
/// Without a render node under <c>/dev/dri</c> (a virtual display such as xvfb, a remote session, a
/// container) WebKit's accelerated compositing and its DMA-BUF renderer have nothing to run on and
/// the page can stay blank. Two WebKit switches turn both off:
/// <c>WEBKIT_DISABLE_COMPOSITING_MODE=1</c> and <c>WEBKIT_DISABLE_DMABUF_RENDERER=1</c>. They are set in
/// the native environment, where WebKit and its web process read them, and never over a value the
/// user set. <c>MAT_WEBKIT_SOFTWARE=1</c> forces the software path and <c>MAT_WEBKIT_SOFTWARE=0</c>
/// forbids it, for a machine the detection gets wrong.
/// </remarks>
internal static class SoftwareRendering
{
    /// <summary>The override variable: 1 forces software drawing, 0 forbids it.</summary>
    public const string OverrideVariable = "MAT_WEBKIT_SOFTWARE";

    /// <summary>True when software drawing applies to this process; computed once by <see cref="Apply"/>.</summary>
    public static bool Active { get; private set; }

    /// <summary>Decides and, when software drawing applies, sets the two WebKit switches.</summary>
    public static void Apply()
    {
        string? forced = Environment.GetEnvironmentVariable(OverrideVariable);
        Active = forced switch
        {
            "1" => true,
            "0" => false,
            _ => !HasRenderNode(),
        };

        if (Active)
        {
            // overwrite = 0: a value the user set stays.
            LibC.setenv("WEBKIT_DISABLE_COMPOSITING_MODE", "1", 0);
            LibC.setenv("WEBKIT_DISABLE_DMABUF_RENDERER", "1", 0);
        }
    }

    private static bool HasRenderNode()
    {
        try
        {
            return Directory.Exists("/dev/dri") && Directory.GetFiles("/dev/dri", "renderD*").Length > 0;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
