// Decides whether 8-bit models must run on the exact matrix kernels: they must on a processor without VNNI.
using System;
using System.Runtime.Intrinsics.X86;

namespace MeetingAudioTranscriptor.Inference;

/// <summary>Whether a session uses the runtime's exact 8-bit matrix multiplication.</summary>
/// <remarks>
/// On an x64 processor without VNNI the runtime's fast 8-bit kernels multiply and add in 16 bits,
/// which saturates, and an 8-bit model then makes more mistakes: the recogniser's German word error
/// rate rose from 8.2 % to 12.4 % under an AVX2-only processor mask. The runtime's precision mode
/// uses kernels that cannot saturate and brought it back to 8.8 %, for about a fifth less speed. On a
/// processor with VNNI the fast kernels are already exact and the mode would only halve the speed,
/// so it is switched on exactly when VNNI is missing. The environment variable
/// <c>MAT_ORT_QUANT_PRECISION</c> (<c>0</c> or <c>1</c>) overrides the choice for a benchmark.
/// </remarks>
public static class QuantizedKernelPrecision
{
    /// <summary>The runtime's session configuration key for the exact kernels.</summary>
    public const string ConfigurationKey = "session.x64quantprecision";

    /// <summary>The environment variable that overrides the automatic choice.</summary>
    public const string OverrideVariable = "MAT_ORT_QUANT_PRECISION";

    /// <summary>True when sessions on this machine are opened with the exact kernels.</summary>
    public static bool Enabled => Decide(Environment.GetEnvironmentVariable(OverrideVariable), HasVnni());

    /// <summary>The choice for a given override text and processor capability.</summary>
    /// <param name="overrideText">The override variable's value, or null when it is not set.</param>
    /// <param name="hasVnni">Whether the processor has a VNNI extension the runtime can use.</param>
    public static bool Decide(string? overrideText, bool hasVnni) => overrideText switch
    {
        "1" => true,
        "0" => false,
        _ => !hasVnni,
    };

    /// <summary>Whether this processor has AVX-VNNI, or AVX-512 VNNI with AVX-512 enabled by the operating system.</summary>
    public static bool HasVnni()
    {
        if (!X86Base.IsSupported)
        {
            // Not an x64 processor: the saturation is specific to the x64 kernels.
            return true;
        }

        if (AvxVnni.IsSupported)
        {
            return true;
        }

        // AVX-512 VNNI is bit 11 of ECX in CPUID leaf 7; it only counts when AVX-512 itself is usable.
        (_, _, int ecx, _) = X86Base.CpuId(7, 0);
        return Avx512F.IsSupported && (ecx & (1 << 11)) != 0;
    }
}
