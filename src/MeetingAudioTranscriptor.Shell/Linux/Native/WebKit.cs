// The WebKitGTK 4.1, JavaScriptCore and libsoup calls the Linux host makes: the web context and its
// custom scheme, the web view and its settings, the message handler, the navigation policy, script
// evaluation and the content filter that blocks every load off the page's origin.
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Shell.Linux.Native;

/// <summary>
/// The WebKitGTK surface the Linux host touches (webkit2/webkit2.h of the 4.1 API, jsc/jsc.h and
/// libsoup/soup.h). The 4.1 API is the GTK 3 one; its script message signal hands over a
/// WebKitJavascriptResult, and script evaluation is <c>webkit_web_view_evaluate_javascript</c>,
/// present since 2.40 and replacing the deprecated <c>run_javascript</c>.
/// </summary>
internal static unsafe partial class WebKit
{
    internal const string Library = "libwebkit2gtk-4.1.so.0";
    internal const string JavaScriptCoreLibrary = "libjavascriptcoregtk-4.1.so.0";

    // libsoup 3 is the network library WebKitGTK 4.1 is built on; only its header list is used here,
    // to hand response headers to a custom scheme response.
    private const string SoupLibrary = "libsoup-3.0.so.0";

    /// <summary>WebKitPolicyDecisionType: a navigation in the current view.</summary>
    internal const int WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION = 0;

    /// <summary>WebKitPolicyDecisionType: a navigation that would open a new window.</summary>
    internal const int WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION = 1;

    /// <summary>WebKitLoadEvent: the load has finished.</summary>
    internal const int WEBKIT_LOAD_FINISHED = 3;

    /// <summary>WebKitHardwareAccelerationPolicy: never composite on the GPU.</summary>
    internal const int WEBKIT_HARDWARE_ACCELERATION_POLICY_NEVER = 2;

    /// <summary>WebKitCacheModel: the smallest cache, for a single local page.</summary>
    internal const int WEBKIT_CACHE_MODEL_DOCUMENT_VIEWER = 0;

    /// <summary>SoupMessageHeadersType: the headers of a response.</summary>
    internal const int SOUP_MESSAGE_HEADERS_RESPONSE = 1;

    [LibraryImport(Library)]
    internal static partial nuint webkit_website_data_manager_get_type();

    [LibraryImport(Library)]
    internal static partial nint webkit_web_context_new_with_website_data_manager(nint manager);

    [LibraryImport(Library)]
    internal static partial void webkit_web_context_set_cache_model(nint context, int model);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_web_context_register_uri_scheme(nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint data, nint destroyData);

    [LibraryImport(Library)]
    internal static partial nint webkit_web_context_get_security_manager(nint context);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_security_manager_register_uri_scheme_as_cors_enabled(nint manager, string scheme);

    [LibraryImport(Library)]
    internal static partial nint webkit_web_view_new_with_context(nint context);

    [LibraryImport(Library)]
    internal static partial nint webkit_web_view_get_settings(nint view);

    [LibraryImport(Library)]
    internal static partial nint webkit_web_view_get_user_content_manager(nint view);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_web_view_load_uri(nint view, string uri);

    [LibraryImport(Library)]
    internal static partial void webkit_web_view_reload(nint view);

    [LibraryImport(Library)]
    internal static partial nint webkit_web_view_get_uri(nint view);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_web_view_evaluate_javascript(nint view, string script, nint length, string? worldName, string? sourceUri, nint cancellable, nint callback, nint data);

    [LibraryImport(Library)]
    internal static partial void webkit_settings_set_enable_developer_extras(nint settings, int enabled);

    [LibraryImport(Library)]
    internal static partial void webkit_settings_set_allow_file_access_from_file_urls(nint settings, int allowed);

    [LibraryImport(Library)]
    internal static partial void webkit_settings_set_allow_universal_access_from_file_urls(nint settings, int allowed);

    [LibraryImport(Library)]
    internal static partial void webkit_settings_set_javascript_can_open_windows_automatically(nint settings, int allowed);

    [LibraryImport(Library)]
    internal static partial void webkit_settings_set_javascript_can_access_clipboard(nint settings, int allowed);

    [LibraryImport(Library)]
    internal static partial void webkit_settings_set_enable_back_forward_navigation_gestures(nint settings, int enabled);

    [LibraryImport(Library)]
    internal static partial void webkit_settings_set_hardware_acceleration_policy(nint settings, int policy);

    [LibraryImport(Library)]
    internal static partial void webkit_settings_set_enable_write_console_messages_to_stdout(nint settings, int enabled);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name);

    [LibraryImport(Library)]
    internal static partial nint webkit_javascript_result_get_js_value(nint result);

    [LibraryImport(Library)]
    internal static partial nint webkit_navigation_policy_decision_get_navigation_action(nint decision);

    [LibraryImport(Library)]
    internal static partial nint webkit_navigation_action_get_request(nint action);

    [LibraryImport(Library)]
    internal static partial nint webkit_uri_request_get_uri(nint request);

    [LibraryImport(Library)]
    internal static partial void webkit_policy_decision_ignore(nint decision);

    [LibraryImport(Library)]
    internal static partial nint webkit_uri_scheme_request_get_uri(nint request);

    [LibraryImport(Library)]
    internal static partial nint webkit_uri_scheme_response_new(nint stream, long length);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_uri_scheme_response_set_status(nint response, uint statusCode, string reasonPhrase);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_uri_scheme_response_set_content_type(nint response, string contentType);

    /// <summary>Hands the headers to the response, which takes ownership of them (transfer full).</summary>
    [LibraryImport(Library)]
    internal static partial void webkit_uri_scheme_response_set_http_headers(nint response, nint headers);

    [LibraryImport(Library)]
    internal static partial void webkit_uri_scheme_request_finish_with_response(nint request, nint response);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint webkit_user_content_filter_store_new(string storagePath);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_user_content_filter_store_save(nint store, string identifier, nint source, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Library)]
    internal static partial nint webkit_user_content_filter_store_save_finish(nint store, nint result, GLib.GError** error);

    [LibraryImport(Library)]
    internal static partial void webkit_user_content_manager_add_filter(nint manager, nint filter);

    [LibraryImport(Library)]
    internal static partial void webkit_user_content_filter_unref(nint filter);

    [LibraryImport(JavaScriptCoreLibrary)]
    internal static partial int jsc_value_is_string(nint value);

    [LibraryImport(JavaScriptCoreLibrary)]
    internal static partial nint jsc_value_to_string(nint value);

    [LibraryImport(SoupLibrary)]
    internal static partial nint soup_message_headers_new(int type);

    [LibraryImport(SoupLibrary, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void soup_message_headers_append(nint headers, string name, string value);
}
