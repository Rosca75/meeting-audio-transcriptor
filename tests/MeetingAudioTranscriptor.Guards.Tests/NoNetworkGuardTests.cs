// Guard: the application is offline, so no source file names a network type.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed class NoNetworkGuardTests
{
    /// <summary>The names of types and namespaces that open or describe a connection.</summary>
    private static readonly string[] Forbidden =
    [
        "HttpClient", "System.Net.Http", "System.Net.Sockets", "WebRequest", "TcpClient",
        "UdpClient", "HttpListener", "WebSocket",
    ];
    /// <summary>Files allowed to mention a forbidden name, each with its reason. Empty: nothing leaves the machine.</summary>
    private static readonly Dictionary<string, string> Exemptions = [];

    [Fact]
    public void NoSourceFileNamesANetworkType()
    {
        IReadOnlyList<string> files = RepositoryFiles.Under("src", "*.cs", "*.csproj");
        Assert.True(files.Count > 0, "got no file under src, wanted at least one; the guard cannot run");

        List<string> offenders = [];
        foreach (string file in files)
        {
            string relative = RepositoryFiles.Relative(file);
            if (Exemptions.ContainsKey(relative))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (string name in Forbidden.Where(name => text.Contains(name, System.StringComparison.Ordinal)))
            {
                offenders.Add($"{relative} names {name}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"got ({string.Join("; ", offenders)}), wanted no network type under src; the application performs no network input or output");
    }
}
