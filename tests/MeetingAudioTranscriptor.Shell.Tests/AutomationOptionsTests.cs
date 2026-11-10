// Tests that the automation hook is off by default and reads its two variables strictly.
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class AutomationOptionsTests
{
    [Fact]
    public void WithNoVariableAutomationIsOff()
    {
        AutomationOptions options = AutomationOptions.FromEnvironment(_ => null);

        Assert.True(!options.Enabled && options.Script is null && options.ExitAfter is null, $"got {options}, wanted automation off");
    }

    [Fact]
    public void TheScriptFileAndTheDelayAreRead()
    {
        string script = Path.Combine(Path.GetTempPath(), "mat-automation-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(script, "window.x = 1;");
        try
        {
            Dictionary<string, string?> variables = new()
            {
                [AutomationOptions.ScriptVariable] = script,
                [AutomationOptions.ExitAfterVariable] = "1500",
            };

            AutomationOptions options = AutomationOptions.FromEnvironment(name => variables.GetValueOrDefault(name));

            Assert.True(options.Enabled, "got automation off, wanted it on");
            Assert.True(options.Script == "window.x = 1;", $"got script '{options.Script}', wanted the file's text");
            Assert.True(options.ExitAfter == TimeSpan.FromMilliseconds(1500), $"got delay {options.ExitAfter}, wanted 1500 ms");
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    [InlineData("1.5")]
    public void ABadDelayIsAStartupError(string delay)
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => AutomationOptions.FromEnvironment(name => name == AutomationOptions.ExitAfterVariable ? delay : null));

        Assert.True(failure.Message.Contains(AutomationOptions.ExitAfterVariable, StringComparison.Ordinal), $"got '{failure.Message}', wanted it to name the variable");
    }

    [Fact]
    public void AMissingScriptIsAStartupErrorNamingIt()
    {
        string missing = Path.Combine(Path.GetTempPath(), "mat-missing-" + Guid.NewGuid().ToString("N") + ".js");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => AutomationOptions.FromEnvironment(name => name == AutomationOptions.ScriptVariable ? missing : null));

        Assert.True(failure.Message.Contains(missing, StringComparison.Ordinal), $"got '{failure.Message}', wanted it to name {missing}");
    }

    [Fact]
    public void TheWrappedScriptWaitsForTheLoadEvent()
    {
        string wrapped = AutomationOptions.WrapForPageLoad("go();");

        Assert.True(wrapped.Contains("\ngo();\n", StringComparison.Ordinal), $"got '{wrapped}', wanted the script inside");
        Assert.True(wrapped.Contains("addEventListener(\"load\"", StringComparison.Ordinal), $"got '{wrapped}', wanted it to wait for the load event");
    }
}
