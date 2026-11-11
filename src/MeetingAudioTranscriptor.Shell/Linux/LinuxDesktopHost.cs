// The Linux desktop host: a GTK 3 window holding a WebKitGTK web view that shows the embedded page
// from a custom scheme, loads nothing else, and carries the bridge's messages both ways.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MeetingAudioTranscriptor.App.Bridge;
using MeetingAudioTranscriptor.Shell.Linux.Native;

namespace MeetingAudioTranscriptor.Shell.Linux;

/// <summary>The Linux implementation of <see cref="IDesktopHost"/>.</summary>
/// <remarks>
/// <para>
/// Everything here runs on the thread that called <see cref="Run"/>, which runs the GTK main loop;
/// other threads reach it through <see cref="GtkMainThread"/>. Native callbacks are static
/// <c>[UnmanagedCallersOnly]</c> functions that find this instance through a GC handle passed as
/// their user data, and an exception escaping one fails fast, because it would otherwise cross
/// into native code.
/// </para>
/// <para>
/// No request leaves the machine. The page is served from the <c>app</c> scheme, which the web
/// context answers from <see cref="EmbeddedAssets"/>. A content filter, compiled before the first
/// load, blocks every load whose address is not on that origin (or a data, blob or about address),
/// every navigation off the origin is ignored by the policy handler, every new window is refused,
/// and the content security policy on each response forbids connections outright.
/// </para>
/// </remarks>
internal sealed unsafe class LinuxDesktopHost : IDesktopHost, IAutomationActions
{
    private const string Title = "Meeting Audio Transcriptor";
    private const string Scheme = "app";
    private const string MessageHandlerName = "host";

    // The size the page is laid out to fit without scrolling; the web view refuses a smaller one.
    private const int InitialWidth = 560;
    private const int InitialHeight = 440;
    private const int MinimumWidth = InitialWidth;
    private const int MinimumHeight = InitialHeight;

    /// <summary>How many times a crashed web process is restarted before the host gives up.</summary>
    private const int ReloadsAllowed = 3;

    /// <summary>
    /// The content filter: block everything, then lift the block for the page's own origin and for
    /// the address kinds that are not network requests. WebKit's filter syntax has no alternation,
    /// so each exception is its own rule.
    /// </summary>
    private const string ContentFilterRules =
        "[" +
        "{\"trigger\":{\"url-filter\":\".*\"},\"action\":{\"type\":\"block\"}}," +
        "{\"trigger\":{\"url-filter\":\"^app://meeting-audio-transcriptor/\"},\"action\":{\"type\":\"ignore-previous-rules\"}}," +
        "{\"trigger\":{\"url-filter\":\"^data:\"},\"action\":{\"type\":\"ignore-previous-rules\"}}," +
        "{\"trigger\":{\"url-filter\":\"^blob:\"},\"action\":{\"type\":\"ignore-previous-rules\"}}," +
        "{\"trigger\":{\"url-filter\":\"^about:\"},\"action\":{\"type\":\"ignore-previous-rules\"}}" +
        "]";

    private static readonly string[] RequiredLibraries =
    [
        Gtk.Library, WebKit.Library, WebKit.JavaScriptCoreLibrary,
    ];

    private LinuxHostServices? _services;
    private PageMessageRouter? _router;
    private Action<string>? _bridgeReceive;
    private AutomationOptions _automation = AutomationOptions.None;
    private GCHandle _self;
    private nint _window;
    private nint _view;
    private nint _context;
    private nint _filterStore;
    private int _exitCode = ShellExitCodes.Ok;
    private bool _closing;
    private bool _scriptRan;
    private int _reloads;

    // The latest URIs a drag over the view carried, keyed by its drag context, so the drop that
    // ends that drag can hand them to the bridge.
    private nint _dragContext;
    private string[] _dragUris = [];
    private nint _pendingDropContext;

    /// <inheritdoc />
    public int Run(Func<IHostServices, Bridge> createBridge, AutomationOptions automation)
    {
        ArgumentNullException.ThrowIfNull(createBridge);
        ArgumentNullException.ThrowIfNull(automation);
        _automation = automation;

        string? missing = FirstMissingLibrary();
        if (missing is not null)
        {
            Console.Error.WriteLine(
                $"the desktop window needs GTK 3 and WebKitGTK 4.1, and {missing} could not be loaded; " +
                "install them (on Debian and Ubuntu: sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0, where Ubuntu 24.04 names the first libgtk-3-0t64), " +
                "or use meeting-audio-transcriptor-cli");
            return ShellExitCodes.RuntimeMissing;
        }

        SoftwareRendering.Apply();

        if (Gtk.gtk_init_check(0, 0) == 0)
        {
            Console.Error.WriteLine(
                "no display could be opened, so the window cannot be shown; run the application from a desktop session " +
                "(DISPLAY or WAYLAND_DISPLAY must be set), under xvfb-run for a headless test, or use meeting-audio-transcriptor-cli");
            return ShellExitCodes.RuntimeMissing;
        }

        GtkMainThread main = GtkMainThread.Install();
        _services = new LinuxHostServices(main);
        _self = GCHandle.Alloc(this);
        try
        {
            Bridge bridge = createBridge(_services);
            _bridgeReceive = bridge.Receive;
            _router = new PageMessageRouter(bridge.Receive, this, automation.Enabled);

            string profile = DataFolder.WebViewProfile(DataFolder.Current());
            Directory.CreateDirectory(profile);

            CreateWindow();
            CreateWebView(profile);
            CompileContentFilterThenLoad(Path.Combine(profile, "content-filters"));

            if (automation.ExitAfter is TimeSpan delay)
            {
                _ = GLib.g_timeout_add((uint)Math.Min(delay.TotalMilliseconds, uint.MaxValue), &OnExitTimer, Self);
            }

            Gtk.gtk_widget_show_all(_window);
            Gtk.gtk_widget_grab_focus(_view);
            Gtk.gtk_main();
            return _exitCode;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Console.Error.WriteLine("the application window could not be set up: " + failure.Message + "; restart the application, and if it keeps failing reinstall WebKitGTK 4.1");
            return ShellExitCodes.StartupFailed;
        }
        finally
        {
            if (_context != 0)
            {
                GLib.g_object_unref(_context);
            }

            if (_filterStore != 0)
            {
                GLib.g_object_unref(_filterStore);
            }

            _self.Free();
        }
    }

    /// <inheritdoc />
    public void Exit(int code)
    {
        _exitCode = code;
        Close();
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
    public void Drop(IReadOnlyList<string> uris) => ForwardDrop(uris);

    private nint Self => GCHandle.ToIntPtr(_self);

    private static LinuxDesktopHost From(nint data) => (LinuxDesktopHost)GCHandle.FromIntPtr(data).Target!;

    private static string? FirstMissingLibrary()
    {
        foreach (string library in RequiredLibraries)
        {
            if (!NativeLibrary.TryLoad(library, out _))
            {
                return library;
            }
        }

        return null;
    }

    private void CreateWindow()
    {
        _window = Gtk.gtk_window_new(Gtk.GTK_WINDOW_TOPLEVEL);
        if (_window == 0)
        {
            throw new InvalidOperationException("GTK could not create the top-level window");
        }

        Gtk.gtk_window_set_title(_window, Title);
        Gtk.gtk_window_set_default_size(_window, InitialWidth, InitialHeight);
        GLib.Connect(_window, "destroy", (nint)(delegate* unmanaged<nint, nint, void>)&OnDestroy, Self);
        _services!.Window = _window;
    }

    /// <summary>
    /// Creates the web context with its data under the profile folder and the app scheme, then the
    /// web view with hardened settings, its policy and drag handlers and the page's message handler.
    /// </summary>
    private void CreateWebView(string profile)
    {
        nint dataManager = CreateDataManager(profile);
        _context = WebKit.webkit_web_context_new_with_website_data_manager(dataManager);
        GLib.g_object_unref(dataManager);
        WebKit.webkit_web_context_set_cache_model(_context, WebKit.WEBKIT_CACHE_MODEL_DOCUMENT_VIEWER);

        // The scheme is secure (a secure context, as the HTTPS origin is on Windows) and CORS-enabled,
        // which is what lets the page's ES modules load from it.
        WebKit.webkit_web_context_register_uri_scheme(_context, Scheme, &OnSchemeRequest, Self, 0);
        nint security = WebKit.webkit_web_context_get_security_manager(_context);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(security, Scheme);
        WebKit.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, Scheme);

        _view = WebKit.webkit_web_view_new_with_context(_context);
        if (_view == 0)
        {
            throw new InvalidOperationException("WebKitGTK could not create the web view");
        }

        nint settings = WebKit.webkit_web_view_get_settings(_view);
        WebKit.webkit_settings_set_allow_file_access_from_file_urls(settings, 0);
        WebKit.webkit_settings_set_allow_universal_access_from_file_urls(settings, 0);
        WebKit.webkit_settings_set_javascript_can_open_windows_automatically(settings, 0);
        WebKit.webkit_settings_set_javascript_can_access_clipboard(settings, 0);
        WebKit.webkit_settings_set_enable_back_forward_navigation_gestures(settings, 0);
        if (SoftwareRendering.Active)
        {
            WebKit.webkit_settings_set_hardware_acceleration_policy(settings, WebKit.WEBKIT_HARDWARE_ACCELERATION_POLICY_NEVER);
        }

        // The page's console stays out of standard output: the web process writes it there in pieces
        // that interleave with the host's own lines, which a test reads one by one.
        WebKit.webkit_settings_set_enable_write_console_messages_to_stdout(settings, 0);
#if DEBUG
        WebKit.webkit_settings_set_enable_developer_extras(settings, 1);
#else
        WebKit.webkit_settings_set_enable_developer_extras(settings, 0);
        GLib.Connect(_view, "context-menu", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, int>)&OnContextMenu, Self);
#endif

        GLib.Connect(_view, "decide-policy", (nint)(delegate* unmanaged<nint, nint, int, nint, int>)&OnDecidePolicy, Self);
        GLib.Connect(_view, "load-changed", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, Self);
        GLib.Connect(_view, "web-process-terminated", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated, Self);
        GLib.Connect(_view, "drag-data-received", (nint)(delegate* unmanaged<nint, nint, int, int, nint, uint, uint, nint, void>)&OnDragDataReceived, Self);
        GLib.Connect(_view, "drag-drop", (nint)(delegate* unmanaged<nint, nint, int, int, uint, nint, int>)&OnDragDrop, Self);

        nint contentManager = WebKit.webkit_web_view_get_user_content_manager(_view);
        if (WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, MessageHandlerName) == 0)
        {
            throw new InvalidOperationException("WebKitGTK refused the page's message handler");
        }

        GLib.Connect(contentManager, "script-message-received::" + MessageHandlerName, (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage, Self);

        Gtk.gtk_widget_set_size_request(_view, MinimumWidth, MinimumHeight);
        Gtk.gtk_container_add(_window, _view);
        _services!.WebView = _view;
        _services.SetPageAttached(true);
    }

    /// <summary>
    /// A website data manager whose data and cache folders are under the profile folder, so the web
    /// view writes nowhere else. Its two folder properties are construct-only, hence the generic
    /// GObject constructor.
    /// </summary>
    private static nint CreateDataManager(string profile)
    {
        string[] names = ["base-data-directory", "base-cache-directory"];
        string[] values = [Path.Combine(profile, "data"), Path.Combine(profile, "cache")];

        GLib.GValue* gvalues = stackalloc GLib.GValue[2];
        byte** gnames = stackalloc byte*[2];
        nint[] allocated = new nint[2];
        try
        {
            for (int i = 0; i < 2; i++)
            {
                gvalues[i] = default;
                GLib.g_value_init(&gvalues[i], GLib.G_TYPE_STRING);
                GLib.g_value_set_string(&gvalues[i], values[i]);
                allocated[i] = Marshal.StringToCoTaskMemUTF8(names[i]);
                gnames[i] = (byte*)allocated[i];
            }

            nint manager = GLib.g_object_new_with_properties(WebKit.webkit_website_data_manager_get_type(), 2, gnames, gvalues);
            if (manager == 0)
            {
                throw new InvalidOperationException("WebKitGTK could not create the website data manager under " + profile);
            }

            return manager;
        }
        finally
        {
            for (int i = 0; i < 2; i++)
            {
                GLib.g_value_unset(&gvalues[i]);
                Marshal.FreeCoTaskMem(allocated[i]);
            }
        }
    }

    /// <summary>
    /// Compiles the content filter into its store, then adds it and loads the page. The load waits for
    /// the filter, because a page loaded first would run for a moment without it.
    /// </summary>
    private void CompileContentFilterThenLoad(string storeFolder)
    {
        Directory.CreateDirectory(storeFolder);
        _filterStore = WebKit.webkit_user_content_filter_store_new(storeFolder);
        byte[] rules = Encoding.UTF8.GetBytes(ContentFilterRules);
        nint bytes;
        fixed (byte* data = rules)
        {
            bytes = GLib.g_bytes_new(data, (nuint)rules.Length);
        }

        WebKit.webkit_user_content_filter_store_save(_filterStore, "offline", bytes, 0, &OnContentFilterSaved, Self);
        GLib.g_bytes_unref(bytes);
    }

    private void OnContentFilterReady(nint result)
    {
        GLib.GError* error = null;
        nint filter = WebKit.webkit_user_content_filter_store_save_finish(_filterStore, result, &error);
        if (filter == 0)
        {
            Console.Error.WriteLine("the filter that keeps the page offline could not be compiled: " + GLib.TakeErrorMessage(error) + "; reinstall WebKitGTK 4.1");
            _exitCode = ShellExitCodes.StartupFailed;
            Close();
            return;
        }

        WebKit.webkit_user_content_manager_add_filter(WebKit.webkit_web_view_get_user_content_manager(_view), filter);
        WebKit.webkit_user_content_filter_unref(filter);
        WebKit.webkit_web_view_load_uri(_view, EmbeddedAssets.LinuxOrigin);
    }

    /// <summary>Answers one request on the app scheme from the embedded page.</summary>
    private void Serve(nint request)
    {
        string uri = GLib.ReadUtf8(WebKit.webkit_uri_scheme_request_get_uri(request));
        AssetResponse asset = EmbeddedAssets.ForUri(uri, EmbeddedAssets.LinuxOrigin);
        if (_automation.Enabled)
        {
            Log($"automation: asset {asset.StatusCode} {uri}");
        }

        nint stream;
        fixed (byte* data = asset.Body)
        {
            nint bytes = GLib.g_bytes_new(data, (nuint)asset.Body.Length);
            stream = GLib.g_memory_input_stream_new_from_bytes(bytes);
            GLib.g_bytes_unref(bytes);
        }

        nint response = WebKit.webkit_uri_scheme_response_new(stream, asset.Body.Length);
        WebKit.webkit_uri_scheme_response_set_status(response, (uint)asset.StatusCode, asset.ReasonPhrase);
        // The bare media type here, and the full one (with its charset) among the headers: once headers
        // are set, WebKit reads the type from them, and a response without one is refused as a style sheet.
        WebKit.webkit_uri_scheme_response_set_content_type(response, asset.MediaType);

        nint headers = WebKit.soup_message_headers_new(WebKit.SOUP_MESSAGE_HEADERS_RESPONSE);
        foreach (KeyValuePair<string, string> header in asset.Headers)
        {
            WebKit.soup_message_headers_append(headers, header.Key, header.Value);
        }

        // The response takes ownership of the headers; the request keeps its own reference to the
        // response and the stream.
        WebKit.webkit_uri_scheme_response_set_http_headers(response, headers);
        WebKit.webkit_uri_scheme_request_finish_with_response(request, response);
        GLib.g_object_unref(response);
        GLib.g_object_unref(stream);
    }

    /// <summary>
    /// Lets a navigation proceed only to the page's origin or a blank page; refuses every new window.
    /// Answers true when the decision was taken here.
    /// </summary>
    private bool DecidePolicy(nint decision, int type)
    {
        if (type == WebKit.WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION)
        {
            WebKit.webkit_policy_decision_ignore(decision);
            return true;
        }

        if (type != WebKit.WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION)
        {
            return false;
        }

        nint action = WebKit.webkit_navigation_policy_decision_get_navigation_action(decision);
        string uri = GLib.ReadUtf8(WebKit.webkit_uri_request_get_uri(WebKit.webkit_navigation_action_get_request(action)));
        if (uri.StartsWith(EmbeddedAssets.LinuxOrigin, StringComparison.OrdinalIgnoreCase) || uri == "about:blank")
        {
            return false;
        }

        WebKit.webkit_policy_decision_ignore(decision);
        if (_automation.Enabled)
        {
            Log("automation: navigation blocked " + uri);
        }

        return true;
    }

    /// <summary>Hands one page message to the router, if the view still shows the page's origin.</summary>
    private void ReceiveScriptMessage(nint javascriptResult)
    {
        string current = GLib.ReadUtf8(WebKit.webkit_web_view_get_uri(_view));
        if (!current.StartsWith(EmbeddedAssets.LinuxOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        nint value = WebKit.webkit_javascript_result_get_js_value(javascriptResult);
        if (value == 0 || WebKit.jsc_value_is_string(value) == 0)
        {
            return;
        }

        string? message = GLib.TakeUtf8(WebKit.jsc_value_to_string(value));
        if (message is not null)
        {
            _router!.Route(message);
        }
    }

    /// <summary>Runs the automation script once, after the first finished load of the page.</summary>
    private void LoadChanged(int loadEvent)
    {
        if (loadEvent != WebKit.WEBKIT_LOAD_FINISHED || _scriptRan || _automation.Script is null)
        {
            return;
        }

        _scriptRan = true;
        Log("automation: page loaded, running the script");
        WebKit.webkit_web_view_evaluate_javascript(_view, AutomationOptions.WrapForPageLoad(_automation.Script), -1, null, null, 0, 0, 0);
    }

    /// <summary>
    /// Remembers the URIs a drag carries. WebKit asks for the drag's data while it moves over the
    /// view (to fire the page's drag events), so by the time of the drop they are already here.
    /// </summary>
    private void DragDataReceived(nint dragContext, nint selection)
    {
        nint list = Gtk.gtk_selection_data_get_uris(selection);
        if (list == 0)
        {
            return;
        }

        List<string> uris = [];
        try
        {
            for (nint* item = (nint*)list; *item != 0; item++)
            {
                uris.Add(GLib.ReadUtf8(*item));
            }
        }
        finally
        {
            GLib.g_strfreev(list);
        }

        _dragContext = dragContext;
        _dragUris = [.. uris];
        if (_pendingDropContext == dragContext)
        {
            _pendingDropContext = 0;
            ForwardDrop(_dragUris);
        }
    }

    /// <summary>
    /// Forwards the dropped files, then lets WebKit finish the drop as usual, so the page's own drop
    /// event and styling run unchanged. A drop whose data has not arrived yet is forwarded when it does.
    /// </summary>
    private void DragDrop(nint dragContext)
    {
        if (_dragContext == dragContext && _dragUris.Length > 0)
        {
            ForwardDrop(_dragUris);
            _dragUris = [];
            _dragContext = 0;
        }
        else
        {
            _pendingDropContext = dragContext;
        }
    }

    /// <summary>The one drop handler: file URIs become local paths, and the paths become the bridge's drop message.</summary>
    private void ForwardDrop(IReadOnlyList<string> uris)
    {
        IReadOnlyList<string> paths = DropEnvelope.PathsFromUris(uris);
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

    private void WebProcessTerminated(int reason)
    {
        if (_reloads < ReloadsAllowed)
        {
            _reloads++;
            Debug.WriteLine($"the web process ended (reason {reason}); reloading the page");
            WebKit.webkit_web_view_reload(_view);
            return;
        }

        Console.Error.WriteLine("the page's web process keeps ending; restart the application, and if it keeps failing reinstall WebKitGTK 4.1");
        _exitCode = ShellExitCodes.StartupFailed;
        Close();
    }

    /// <summary>Destroys the window once; its destroy handler ends the main loop.</summary>
    private void Close()
    {
        if (_closing || _window == 0)
        {
            return;
        }

        _closing = true;
        Gtk.gtk_widget_destroy(_window);
    }

    private void Destroyed()
    {
        _closing = true;
        _services?.SetPageAttached(false);
        _services!.WebView = 0;
        Gtk.gtk_main_quit();
    }

    // The native entry points. Each finds the instance and runs one method; an exception escaping
    // one would unwind into GTK, so it fails fast with the cause instead.

    [UnmanagedCallersOnly]
    private static void OnDestroy(nint widget, nint data) => Guard(() => From(data).Destroyed());

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data) => Guard(() => From(data).Serve(request));

    [UnmanagedCallersOnly]
    private static int OnDecidePolicy(nint view, nint decision, int type, nint data)
    {
        bool handled = false;
        Guard(() => handled = From(data).DecidePolicy(decision, type));
        return handled ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint data) => Guard(() => From(data).LoadChanged(loadEvent));

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint result, nint data) => Guard(() => From(data).ReceiveScriptMessage(result));

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint view, int reason, nint data) => Guard(() => From(data).WebProcessTerminated(reason));

    [UnmanagedCallersOnly]
    private static void OnDragDataReceived(nint widget, nint dragContext, int x, int y, nint selection, uint info, uint time, nint data) =>
        Guard(() => From(data).DragDataReceived(dragContext, selection));

    [UnmanagedCallersOnly]
    private static int OnDragDrop(nint widget, nint dragContext, int x, int y, uint time, nint data)
    {
        Guard(() => From(data).DragDrop(dragContext));

        // Not handled here: WebKit's own handler finishes the drop and fires the page's drop event.
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnContextMenu(nint view, nint menu, nint gdkEvent, nint hitTest, nint data) => 1;

    [UnmanagedCallersOnly]
    private static void OnContentFilterSaved(nint store, nint result, nint data) => Guard(() => From(data).OnContentFilterReady(result));

    [UnmanagedCallersOnly]
    private static int OnExitTimer(nint data)
    {
        Guard(() =>
        {
            LinuxDesktopHost host = From(data);
            host.Log("automation: exit timer elapsed");
            host.Close();
        });
        return GLib.G_SOURCE_REMOVE;
    }

    private static void Guard(Action work)
    {
        try
        {
            work();
        }
        catch (Exception failure)
        {
            Environment.FailFast("unhandled exception in a GTK callback: " + failure.Message, failure);
        }
    }
}
