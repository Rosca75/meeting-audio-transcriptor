// The command-line switches the Windows host passes to the WebView2 browser process to switch off the
// browser engine's own background traffic. Kept free of any web view type, so the Linux suites can
// assert the exact list.
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>The browser switches that keep the WebView2 runtime from reaching out on its own.</summary>
/// <remarks>
/// The page itself can reach nothing (the request filter and the content security policy see to it),
/// but the browser engine under it runs services of its own that are not page requests: the component
/// updater, the field-trial and variations fetch, domain reliability reports, sync, hyperlink pings,
/// metrics upload, and Edge's SmartScreen and optimisation hints. Each switch below is a documented
/// Chromium switch (its definition is named) or a feature named in Microsoft's WebView2 browser-flag
/// list. Outside this application's reach: the Evergreen runtime's own updater, a machine-wide Windows
/// service the application does not start, and anything a machine policy forces on.
/// </remarks>
internal static class BrowserArguments
{
    /// <summary>The switches, one per entry, in the order they are passed.</summary>
    public static IReadOnlyList<string> Switches { get; } =
    [
        // chrome_switches.h kDisableBackgroundNetworking: the subsystems that run network requests in the background.
        "--disable-background-networking",
        // chrome_switches.h kDisableComponentUpdate: no component download or update check.
        "--disable-component-update",
        // chrome_switches.h kDisableDomainReliability: no domain reliability monitoring reports.
        "--disable-domain-reliability",
        // components/sync command_line_switches.h kDisableSync: no syncing of browser data to an account.
        "--disable-sync",
        // components/metrics metrics_switches.h kMetricsRecordingOnly: metrics are recorded and the report dropped.
        "--metrics-recording-only",
        // chrome_switches.h kNoPings: no hyperlink auditing pings.
        "--no-pings",
        // chrome_switches.h kNoFirstRun, also in the WebView2 flag list: no first-run tasks.
        "--no-first-run",
        // base_switches.h kDisableFeatures. msSmartScreenProtection is in the WebView2 flag list;
        // OptimizationHints (optimization_guide_features.cc) fetches hints from a server; MediaRouter
        // (media_router_feature.cc) searches the local network for cast receivers.
        "--disable-features=msSmartScreenProtection,OptimizationHints,MediaRouter",
    ];

    /// <summary>The switches as the one string <c>AdditionalBrowserArguments</c> takes.</summary>
    public static string CommandLine => string.Join(' ', Switches);
}
