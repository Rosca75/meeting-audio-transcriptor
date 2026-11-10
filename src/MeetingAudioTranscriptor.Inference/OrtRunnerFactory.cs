// Opens model files as runners: builds the session settings and turns every load failure into one readable sentence.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;
using Microsoft.ML.OnnxRuntime;

namespace MeetingAudioTranscriptor.Inference;

/// <summary>Creates <see cref="ITensorRunner"/> instances over ONNX model files.</summary>
public static class OrtRunnerFactory
{
    /// <summary>The session configuration key that lets the intra-op threads busy-wait between operators.</summary>
    private const string AllowSpinningKey = "session.intra_op.allow_spinning";

    private static readonly Lock CheckLock = new();
    private static string? _nativeFailure;
    private static bool _checked;

    /// <summary>The version string of the native ONNX Runtime library in use.</summary>
    /// <exception cref="ModelLoadException">The native library is missing or cannot be loaded.</exception>
    public static string RuntimeVersion
    {
        get
        {
            ThrowIfNativeRuntimeUnavailable();
            return OrtEnv.Instance().GetVersionString();
        }
    }

    /// <summary>
    /// Says whether the native ONNX Runtime library can be loaded, as one sentence naming what to do;
    /// null when it can. The answer is computed once per process, so a front end can call it at
    /// start-up to disable model controls with a readable reason instead of failing on first use.
    /// </summary>
    public static string? CheckNativeRuntime()
    {
        lock (CheckLock)
        {
            if (!_checked)
            {
                _nativeFailure = ProbeNativeLibrary();
                _checked = true;
            }

            return _nativeFailure;
        }
    }

    /// <summary>Opens one model file as a runner.</summary>
    /// <param name="modelPath">The path of the <c>.onnx</c> file.</param>
    /// <param name="options">The session settings.</param>
    /// <exception cref="ModelLoadException">The file is missing or not a loadable model, or the native runtime is unavailable.</exception>
    public static ITensorRunner Open(string modelPath, RunnerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(options);
        if (options.IntraOpThreads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.IntraOpThreads, "IntraOpThreads must be at least 1; pass the number of cores one Run call may use.");
        }

        if (!File.Exists(modelPath))
        {
            throw new ModelLoadException(
                $"the model file {modelPath} does not exist; fetch the models with node build/fetch-models.mjs (it writes the models folder the application reads) or point MAT_MODELS_DIR at the folder that holds them");
        }

        ThrowIfNativeRuntimeUnavailable();

        using SessionOptions session = new()
        {
            // One intra-op thread by default: the caller runs several workers on one session, so the
            // workers' count, not each call's, should match the machine's cores.
            IntraOpNumThreads = options.IntraOpThreads,

            // Inter-op parallelism only helps graphs with parallel branches; these graphs are
            // sequential, and a second pool would only add idle threads.
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,

            GraphOptimizationLevel = options.GraphOptimization,

            // The arena keeps freed buffers for the next call, which removes allocation from the
            // hot loop of a decoder that calls the graph tens of thousands of times; the cost is
            // a higher resident size, which the three-gigabyte budget leaves room for.
            EnableCpuMemArena = true,

            // Reuses the memory plan when the input shapes repeat, which they do for fixed-size
            // chunks. A changed shape only makes the runtime plan again, so it is safe to leave on.
            EnableMemoryPattern = true,

            // Only errors are reported, and the runtime writes them to the process's output, so a
            // quieter level keeps the command-line tool's output the transcript's alone.
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        session.AddSessionConfigEntry(AllowSpinningKey, options.AllowSpinning ? "1" : "0");
        if (options.OptimizedModelCachePath is { Length: > 0 } cachePath)
        {
            session.OptimizedModelFilePath = cachePath;
        }

        try
        {
            return new OrtTensorRunner(new InferenceSession(modelPath, session), Path.GetFileName(modelPath));
        }
        catch (Exception e) when (e is OnnxRuntimeException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException)
        {
            throw new ModelLoadException(
                $"the ONNX Runtime could not load {modelPath}: {e.Message}. Fetch the model again with node build/fetch-models.mjs; "
                + "if every model fails the same way, reinstall the application so the runtime library is replaced.",
                e);
        }
    }

    private static void ThrowIfNativeRuntimeUnavailable()
    {
        string? failure = CheckNativeRuntime();
        if (failure is not null)
        {
            throw new ModelLoadException(failure);
        }
    }

    /// <summary>Tries to load the native library the managed package calls into.</summary>
    private static string? ProbeNativeLibrary()
    {
        string file = OperatingSystem.IsWindows() ? "onnxruntime.dll"
            : OperatingSystem.IsMacOS() ? "libonnxruntime.dylib"
            : "libonnxruntime.so";
        Assembly package = typeof(OrtEnv).Assembly;
        try
        {
            // The probe uses the same search the package's own calls use (the application folder
            // first), so a library that loads here loads for the session as well.
            if (NativeLibrary.TryLoad("onnxruntime", package, DllImportSearchPath.AssemblyDirectory, out nint handle))
            {
                NativeLibrary.Free(handle);
                return null;
            }
        }
        catch (Exception e) when (e is ArgumentException or BadImageFormatException)
        {
            return $"the ONNX Runtime library {file} could not be loaded ({e.Message}); reinstall the application, and check that the library matches this machine's architecture.";
        }

        return $"the ONNX Runtime library {file} was not found beside the application ({AppContext.BaseDirectory}); "
            + "reinstall the application, or copy the library from the application's release package next to the executable.";
    }
}
