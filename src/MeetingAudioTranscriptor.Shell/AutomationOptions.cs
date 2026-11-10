// The test hook of both hosts: a script evaluated in the page once it has loaded, and a delay after
// which the window closes itself. Both are off unless their environment variables are set.
using System;
using System.Globalization;
using System.IO;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>
/// What a test asked the host to do, read from the environment at start-up. With neither variable
/// set the host behaves as shipped and ignores every automation message the page might send.
/// </summary>
/// <param name="Script">The JavaScript evaluated in the page after it has loaded, or null.</param>
/// <param name="ExitAfter">The delay after which the window closes itself, or null.</param>
internal sealed record AutomationOptions(string? Script, TimeSpan? ExitAfter)
{
    /// <summary>The variable naming the script file.</summary>
    public const string ScriptVariable = "MAT_UI_AUTOMATION";

    /// <summary>The variable holding the delay in milliseconds.</summary>
    public const string ExitAfterVariable = "MAT_UI_EXIT_AFTER_MS";

    /// <summary>No automation: the shipped behaviour.</summary>
    public static readonly AutomationOptions None = new(null, null);

    /// <summary>True when a test drives this process, which is when automation messages are honoured.</summary>
    public bool Enabled => Script is not null || ExitAfter is not null;

    /// <summary>Reads the two variables; a script file that cannot be read is a start-up error naming it.</summary>
    public static AutomationOptions FromEnvironment(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        string? script = null;
        string? scriptPath = environment(ScriptVariable);
        if (!string.IsNullOrEmpty(scriptPath))
        {
            try
            {
                script = File.ReadAllText(scriptPath);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"the automation script {scriptPath} named by {ScriptVariable} could not be read ({failure.Message}); fix the path or unset the variable",
                    failure);
            }
        }

        TimeSpan? exitAfter = null;
        string? delay = environment(ExitAfterVariable);
        if (!string.IsNullOrEmpty(delay))
        {
            if (!int.TryParse(delay, NumberStyles.None, CultureInfo.InvariantCulture, out int milliseconds) || milliseconds <= 0)
            {
                throw new InvalidOperationException(
                    $"{ExitAfterVariable} is '{delay}', wanted a positive whole number of milliseconds; fix the value or unset the variable");
            }

            exitAfter = TimeSpan.FromMilliseconds(milliseconds);
        }

        return new AutomationOptions(script, exitAfter);
    }

    /// <summary>
    /// Wraps a test's script so it runs once the page's own modules have run: a web view reports its
    /// load as finished as soon as the document has, which can be before a module script has executed,
    /// while the window's load event always comes after it.
    /// </summary>
    public static string WrapForPageLoad(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return "(function () { const run = function () {\n" + script + "\n}; " +
            "if (document.readyState === \"complete\") { setTimeout(run, 0); } " +
            "else { window.addEventListener(\"load\", function () { setTimeout(run, 0); }, { once: true }); } })();";
    }
}
