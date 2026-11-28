using System.Runtime.Versioning;
using AgentSandbox.Mcp;

namespace AgentSandbox.Tests;

// Unix file modes and symlinks; the setup runs on macOS, CI on Linux
[UnsupportedOSPlatform("windows")]
public sealed class EmbeddedMcpScriptsTests : IDisposable
{
	private readonly string _dataDirectory = Directory.CreateTempSubdirectory("agent-sandbox-tests-").FullName;

	public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

	private string Scripts => Path.Combine(_dataDirectory, "agent-sandbox", "mcp", "1.2.3");

	[Fact]
	public void Embeds_both_launchers()
	{
		EmbeddedMcpScripts.Names.Should().Equal("grafana-mcp", "keychain-env");
	}

	[Fact]
	public async Task Writes_executable_scripts_once()
	{
		(await ExtractAsync()).Should().Be((Scripts, true));
		(await ExtractAsync()).Should().Be((Scripts, false));

		Directory.GetFiles(Scripts).Select(Path.GetFileName).Should().BeEquivalentTo("grafana-mcp", "keychain-env");
		Directory.GetFiles(Path.GetDirectoryName(Scripts)!).Should().BeEmpty("temporary files are renamed into place");
	}

	[Fact]
	public async Task Rewrites_a_changed_script_restores_its_mode_and_deletes_other_files()
	{
		await ExtractAsync();
		var script = Path.Combine(Scripts, "grafana-mcp");
		var expected = File.ReadAllBytes(script);
		File.WriteAllText(script, "changed");
		File.SetUnixFileMode(Path.Combine(Scripts, "keychain-env"), UnixFileMode.UserRead);
		File.WriteAllText(Path.Combine(Scripts, "removed-script"), "old");

		(await ExtractAsync()).Changed.Should().BeTrue();

		File.ReadAllBytes(script).Should().Equal(expected);
		File.GetUnixFileMode(Path.Combine(Scripts, "keychain-env")).Should().Be((UnixFileMode)0b111_101_101);
		Directory.GetFiles(Scripts).Select(Path.GetFileName).Should().BeEquivalentTo("grafana-mcp", "keychain-env");
	}

	[Fact]
	public async Task Removes_the_scripts_of_other_tool_versions()
	{
		var old = Path.Combine(_dataDirectory, "agent-sandbox", "mcp", "1.2.2");
		Directory.CreateDirectory(old);
		File.WriteAllText(Path.Combine(old, "grafana-mcp"), "old");

		await ExtractAsync();

		Directory.GetDirectories(Path.GetDirectoryName(Scripts)!).Should().Equal(Scripts);
	}

	private Task<(string Directory, bool Changed)> ExtractAsync() =>
		EmbeddedMcpScripts.ExtractAsync(_dataDirectory, "1.2.3", TestContext.Current.CancellationToken);
}
