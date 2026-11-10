// The Windows desktop host: a raw Win32 window holding a WebView2 control that shows the embedded
// page from a virtual HTTPS origin, loads nothing else, and carries the bridge's messages both ways.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App.Bridge;
using MeetingAudioTranscriptor.Shell.Windows.Native;
using Microsoft.Web.WebView2.Core;

namespace MeetingAudioTranscriptor.Shell.Windows;

/// <summary>The Windows implementation of <see cref="IDesktopHost"/>.</summary>
/// <remarks>
/// <para>
/// The thread that calls <see cref="Run"/> is the interface thread: it creates the window, owns the
/// pump and never waits on a task. The web view's creation is an asynchronous method whose
/// continuations come back through <see cref="UiThread"/>, so every WebView2 call is on that thread.
/// </para>
/// <para>
/// No request leaves the machine. Every http and https request of the web view goes through
/// <c>WebResourceRequested</c>, which answers from <see cref="EmbeddedAssets"/> for the page's origin and
/// with a 404 for every other address; a navigation off the origin is cancelled, a new window is
/// refused, downloads and permissions are refused, the reputation check that would send addresses to
/// a web service is off, and the content security policy on each response forbids connections.
/// </para>
/// </remarks>
internal sealed class WindowsDesktopHost : IDesktopHost, IAutomationActions
{
    private const string Title = "Meeting Audio Transcriptor";
    private const int InitialWidth = 900;
    private const int InitialHeight = 720;

    /// <summary>Where Microsoft publishes the Evergreen runtime.</summary>
    private const string RuntimeDownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/";

    private WindowsHostServices? _services;
    private MainWindow? _window;
    private PageMessageRouter? _router;
    private Action<string>? _bridgeReceive;
    private AutomationOptions _automation = AutomationOptions.None;
    private bool _scriptRan;

    /// <inheritdoc />
    public int Run(Func<IHostServices, Bridge> createBridge, AutomationOptions automation)
    {
        ArgumentNullException.ThrowIfNull(createBridge);
        ArgumentNullException.ThrowIfNull(automation);
        _automation = automation;

        // Per-monitor awareness before any window exists; the manifest declares the same, and this
        // call is the fallback when the manifest was not embedded.
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        string? missing = MissingRuntimeReason();
        if (missing is not null)
        {
            Win32.MessageBoxW(
                0,
                "The Microsoft Edge WebView2 runtime is not installed (" + missing + "), and the window cannot open without it. It ships with Windows 11 and is a free download for other versions: " + RuntimeDownloadPage,
                Title,
                Win32.MB_OK | Win32.MB_ICONERROR);
            return ShellExitCodes.RuntimeMissing;
        }

        try
        {
            UiThread ui = UiThread.Install();
            _services = new WindowsHostServices(ui);
            Bridge bridge = createBridge(_services);
            _bridgeReceive = bridge.Receive;
            _router = new PageMessageRouter(bridge.Receive, this, automation.Enabled);

            _window = MainWindow.Create(Title, InitialWidth, InitialHeight);
            _services.Window = _window;
            _window.Closed += () => _services.SetPageAttached(false);

            // The start-up continues on the pump; a failure there ends the loop with its code.
            _ = StartAsync(_window);
            if (automation.ExitAfter is TimeSpan delay)
            {
                _ = CloseAfterAsync(delay);
            }

            _window.Show();
            return UiThread.RunLoop();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Win32.MessageBoxW(0, "The application window could not be set up: " + failure.Message + " Restart the application.", Title, Win32.MB_OK | Win32.MB_ICONERROR);
            return ShellExitCodes.StartupFailed;
        }
    }

    /// <inheritdoc />
    public void Exit(int code)
    {
        if (_window is not null)
        {
            _window.ExitCode = code;
            _window.Close();
        }
    }

    /// <inheritdoc />
    public void Log(string line)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }

    /// <inheritdoc />
    public void Emit(string json) => _services?.Emit(json);

    /// <inheritdoc />
    public void Drop(IReadOnlyList<string> uris) => ForwardDrop(DropEnvelope.PathsFromUris(uris));

    /// <summary>Null when the Evergreen runtime can be found, else why not; the lookup throws when it cannot.</summary>
    private static string? MissingRuntimeReason()
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
            return null;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return "no runtime was found";
        }
        catch (DllNotFoundException)
        {
            return "WebView2Loader.dll is missing beside the application";
        }
    }

    /// <summary>
    /// Creates the environment, the controller and the web view with its settings, its request
    /// handler and its message handler, in that order, and only then navigates, so the first request
    /// already meets the handler. A failure is shown and ends the loop.
    /// </summary>
    private async Task StartAsync(MainWindow window)
    {
        try
        {
            string profile = DataFolder.WebViewProfile(DataFolder.Current());
            Directory.CreateDirectory(profile);
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, profile, null);
            CoreWebView2Controller controller = await environment.CreateCoreWebView2ControllerAsync(window.Handle);
            try
            {
                Configure(environment, controller.CoreWebView2);
                (int width, int height) = window.ClientSize;
                controller.Bounds = new Rectangle(0, 0, width, height);
                window.Resized += (w, h) => controller.Bounds = new Rectangle(0, 0, w, h);
                window.Focused += () => controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
                window.Closed += () => controller.Close();

                _services!.WebView = controller.CoreWebView2;
                _services.SetPageAttached(true);
                controller.CoreWebView2.Navigate(EmbeddedAssets.WindowsOrigin);
            }
            catch
            {
                // A controller never handed over would keep its browser process alive with no window.
                controller.Close();
                throw;
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Win32.MessageBoxW(
                window.Handle,
                "The application window could not be set up: " + failure.Message + " Restart the application; if it keeps failing, reinstall the Microsoft Edge WebView2 runtime from " + RuntimeDownloadPage,
                Title,
                Win32.MB_OK | Win32.MB_ICONERROR);
            window.ExitCode = ShellExitCodes.StartupFailed;
            window.Close();
        }
    }

    private void Configure(CoreWebView2Environment environment, CoreWebView2 core)
    {
        CoreWebView2Settings settings = core.Settings;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsReputationCheckingRequired = false;
        bool debugBuild = false;
#if DEBUG
        debugBuild = true;
#endif
        settings.AreDevToolsEnabled = debugBuild;
        settings.AreDefaultContextMenusEnabled = debugBuild;
        settings.AreBrowserAcceleratorKeysEnabled = debugBuild;

        // Every http and https request, from any context and any source, is answered here.
        core.AddWebResourceRequestedFilter("http://*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.AddWebResourceRequestedFilter("https://*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) =>
        {
            AssetResponse asset = EmbeddedAssets.ForUri(args.Request.Uri, EmbeddedAssets.WindowsOrigin);
            if (_automation.Enabled)
            {
                Log($"automation: asset {asset.StatusCode} {args.Request.Uri}");
            }

            args.Response = environment.CreateWebResourceResponse(
                new MemoryStream(asset.Body), asset.StatusCode, asset.ReasonPhrase, ResponseHeaders.AsBlock(asset.ContentType));
        };

        core.NavigationStarting += (_, args) => CancelUnlessOnOrigin(args.Uri, () => args.Cancel = true);
        core.FrameNavigationStarting += (_, args) => CancelUnlessOnOrigin(args.Uri, () => args.Cancel = true);
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.WebMessageReceived += (_, args) => OnWebMessage(args);
        core.NavigationCompleted += (_, args) => RunAutomationScript(core, args.IsSuccess);
        core.ProcessFailed += (_, args) => OnProcessFailed(core, args.ProcessFailedKind);
    }

    private void CancelUnlessOnOrigin(string uri, Action cancel)
    {
        if (uri.StartsWith(EmbeddedAssets.WindowsOrigin, StringComparison.OrdinalIgnoreCase) || uri == "about:blank")
        {
            return;
        }

        cancel();
        if (_automation.Enabled)
        {
            Log("automation: navigation blocked " + uri);
        }
    }

    /// <summary>
    /// One message from the page. A message carrying file objects is a drop: their paths become the
    /// bridge's drop message, and the page's own marker text goes no further. Anything else goes to the
    /// router as the string it is.
    /// </summary>
    private void OnWebMessage(CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!args.Source.StartsWith(EmbeddedAssets.WindowsOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IReadOnlyList<object>? objects = args.AdditionalObjects;
        if (objects is not null && objects.Count > 0)
        {
            List<string> paths = [];
            foreach (object item in objects)
            {
                if (item is CoreWebView2File file && !string.IsNullOrEmpty(file.Path))
                {
                    paths.Add(file.Path);
                }
            }

            ForwardDrop(paths);
            return;
        }

        string text;
        try
        {
            text = args.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // The page posts strings; an object is handed over as its JSON text.
            text = args.WebMessageAsJson;
        }

        _router!.Route(text);
    }

    private void ForwardDrop(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        string envelope = DropEnvelope.FromPaths(paths);
        if (_automation.Enabled)
        {
            Log("automation: drop forwarded " + envelope);
        }

        _bridgeReceive!(envelope);
    }

    private void RunAutomationScript(CoreWebView2 core, bool succeeded)
    {
        if (!succeeded || _scriptRan || _automation.Script is null)
        {
            return;
        }

        _scriptRan = true;
        Log("automation: page loaded, running the script");
        _ = core.ExecuteScriptAsync(AutomationOptions.WrapForPageLoad(_automation.Script));
    }

    /// <summary>
    /// A lost renderer is reloaded once the page is gone or frozen; a lost browser process ends the
    /// application, because nothing recovers it; helper processes are restarted by the web view itself.
    /// </summary>
    private void OnProcessFailed(CoreWebView2 core, CoreWebView2ProcessFailedKind kind)
    {
        switch (kind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                Win32.MessageBoxW(
                    _window?.Handle ?? 0,
                    "The web view's browser process has stopped, and the application cannot continue. Restart the application.",
                    Title,
                    Win32.MB_OK | Win32.MB_ICONERROR);
                Exit(ShellExitCodes.StartupFailed);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                core.Reload();
                break;
            default:
                break;
        }
    }

    private async Task CloseAfterAsync(TimeSpan delay)
    {
        // The continuation resumes on the interface thread through its context.
        await Task.Delay(delay);
        Log("automation: exit timer elapsed");
        _window?.Close();
    }
}
