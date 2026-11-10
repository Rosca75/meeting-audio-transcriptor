// Guard: the engine assembly references only the framework and the contracts project.
using System.Linq;
using System.Reflection;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed class EngineReferencesGuardTests
{
    [Fact]
    public void EngineReferencesOnlyTheFrameworkAndTheContracts()
    {
        Assembly engine = Assembly.Load("MeetingAudioTranscriptor.Engine");
        string[] references = engine.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToArray();

        Assert.True(references.Length > 0, "got no referenced assembly, wanted at least the framework; the guard cannot run");
        string[] offenders = references.Where(name => !IsAllowed(name)).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"got engine references to ({string.Join(", ", offenders)}), wanted only System.*, netstandard, mscorlib and MeetingAudioTranscriptor.Contracts; move that dependency into the Inference or Media project");
    }

    private static bool IsAllowed(string name)
    {
        return name == "System"
            || name.StartsWith("System.", System.StringComparison.Ordinal)
            || name == "netstandard"
            || name == "mscorlib"
            || name == "MeetingAudioTranscriptor.Contracts";
    }
}
