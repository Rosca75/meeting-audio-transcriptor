// The bound surface: one method per bridge call, over the host seam and the transcriber, with no
// knowledge of envelopes or windows.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>What the page can do, as plain methods the dispatcher binds to by name.</summary>
internal sealed class BridgeSurface
{
    private readonly IHostServices _host;
    private readonly ITranscriber _transcriber;
    private readonly string _version;
    private readonly BridgeEmitter _emitter;
    private readonly RunCoordinator _run;

    public BridgeSurface(IHostServices host, ITranscriber transcriber, string version, BridgeEmitter emitter, TimeProvider clock)
    {
        _host = host;
        _transcriber = transcriber;
        _version = version;
        _emitter = emitter;
        _run = new RunCoordinator(transcriber, emitter, clock);
    }

    /// <summary>The version, the supported extensions and the models check.</summary>
    public AppInfoResult AppInfo()
    {
        ModelCheckResult models;
        try
        {
            models = _transcriber.CheckModels();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            models = new ModelCheckResult
            {
                Ready = false,
                Message = "The models could not be checked: " + Faults.Sentence("The models check", failure)
                    + " Reinstall the application if the problem remains.",
            };
        }

        return new AppInfoResult { Version = _version, Extensions = SupportedFiles.Extensions, Models = models };
    }

    /// <summary>Opens the file dialog; resolves to the selection, or null when cancelled or refused.</summary>
    public async Task<FileSelection?> PickFileAsync()
    {
        string? path = await AskHost("The file dialog", _host.PickAudioFileAsync).ConfigureAwait(false);
        if (path is null)
        {
            return null;
        }

        string? refusal = SupportedFiles.Select([path], out FileSelection? selection);
        if (refusal is not null)
        {
            _emitter.Event(BridgeEvents.FileRefused, new FileRefusal { Reason = refusal }, BridgeJson.Context.FileRefusal);
        }

        return selection;
    }

    /// <summary>
    /// Receives the names the page saw dropped. When none is supported the refusal goes out at once,
    /// naming them; the resolved paths arrive separately through <see cref="HandleDrop"/>.
    /// </summary>
    public void FilesDropped(string[] names)
    {
        if (names.Length > 0 && !names.Any(SupportedFiles.IsSupported))
        {
            _emitter.Event(BridgeEvents.FileRefused, new FileRefusal { Reason = SupportedFiles.Refusal(names) }, BridgeJson.Context.FileRefusal);
        }
    }

    /// <summary>Turns the host's resolved drop paths into file:selected or file:refused.</summary>
    public void HandleDrop(DropMessage message)
    {
        string? refusal = SupportedFiles.Select(message.Drop?.Paths ?? [], out FileSelection? selection);
        if (refusal is not null)
        {
            _emitter.Event(BridgeEvents.FileRefused, new FileRefusal { Reason = refusal }, BridgeJson.Context.FileRefusal);
            return;
        }

        _emitter.Event(BridgeEvents.FileSelected, selection!, BridgeJson.Context.FileSelection);
    }

    /// <summary>Opens the folder dialog; resolves to the path, or null when cancelled.</summary>
    public Task<string?> PickFolderAsync(string? initialFolder)
    {
        string? start = !string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder) ? initialFolder : null;
        return AskHost("The folder dialog", () => _host.PickFolderAsync(start));
    }

    /// <summary>Shows a folder in the system file manager.</summary>
    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            throw new ArgumentException($"The folder \"{folderPath}\" does not exist. Run the transcription again, or choose another output folder.");
        }

        try
        {
            _host.OpenFolder(folderPath);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            throw new InvalidOperationException(
                $"The folder could not be shown ({failure.Message}). Open \"{folderPath}\" in your file manager instead.", failure);
        }
    }

    /// <summary>Starts a transcription; throws a sentence when the request is invalid or a run is active.</summary>
    public void StartRun(TranscribeRequest request)
    {
        _run.Start(request);
    }

    /// <summary>Cancels the running transcription, if any.</summary>
    public void CancelRun()
    {
        _run.Cancel();
    }

    // A dialog that fails to open becomes one sentence with a way forward, not a raw platform message.
    private static async Task<string?> AskHost(string what, Func<Task<string?>> dialog)
    {
        try
        {
            return await dialog().ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            throw new InvalidOperationException(
                $"{what} could not be opened ({failure.Message}). Try again, and if it keeps failing restart the application.", failure);
        }
    }
}
