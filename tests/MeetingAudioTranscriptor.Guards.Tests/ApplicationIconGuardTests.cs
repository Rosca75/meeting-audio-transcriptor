// Guard: the Windows desktop executable embeds the application's icon, because without it Explorer,
// the title bar and the taskbar show the stock icon of an unnamed program.
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed partial class ApplicationIconGuardTests
{
    private const string Project = "src/MeetingAudioTranscriptor.Shell/MeetingAudioTranscriptor.Shell.csproj";

    [GeneratedRegex(@"<ApplicationIcon>([^<]+)</ApplicationIcon>")]
    private static partial Regex ApplicationIcon();

    [Fact]
    public void TheShellProjectNamesAnIconFileThatHoldsTheSizesWindowsAsksFor()
    {
        string project = File.ReadAllText(Path.Combine(RepositoryFiles.Root, Project));
        Match declared = ApplicationIcon().Match(project);
        Assert.True(declared.Success, $"got no <ApplicationIcon> in {Project}, wanted Windows/app.ico, the file build/make-app-icon.mjs writes");

        string icon = Path.Combine(RepositoryFiles.Root, "src", "MeetingAudioTranscriptor.Shell", declared.Groups[1].Value);
        Assert.True(File.Exists(icon), $"got no file at {declared.Groups[1].Value} beside {Project}; run node build/make-app-icon.mjs");

        byte[] bytes = File.ReadAllBytes(icon);
        Assert.True(bytes.Length > 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0, $"got a file at {declared.Groups[1].Value} that is not an icon; run node build/make-app-icon.mjs");

        int count = bytes[4] | (bytes[5] << 8);
        var sizes = new System.Collections.Generic.List<int>();
        for (int index = 0; index < count; index++)
        {
            int width = bytes[6 + index * 16];
            sizes.Add(width == 0 ? 256 : width);
        }

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], sizes);
    }
}
