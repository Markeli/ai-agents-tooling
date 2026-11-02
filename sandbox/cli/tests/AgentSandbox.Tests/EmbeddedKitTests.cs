using System.Runtime.CompilerServices;

namespace AgentSandbox.Tests;

public sealed class EmbeddedKitTests : IDisposable
{
	private readonly string _dataDirectory = Directory.CreateTempSubdirectory("agent-sandbox-tests-").FullName;

	public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

	[Fact]
	public void Writes_the_kit_of_this_repository_under_a_directory_per_version()
	{
		var kit = EmbeddedKit.Extract(_dataDirectory, "1.2.3");

		kit.Should().Be(Path.Combine(_dataDirectory, "agent-sandbox", "kits", "1.2.3", "markeli-claude"));
		File.ReadAllText(Path.Combine(kit, "spec.yaml")).Should().Be(File.ReadAllText(Path.Combine(RepositoryKit(), "spec.yaml")));
		Directory.EnumerateFiles(kit, "*", SearchOption.AllDirectories).Select(Path.GetFileName)
			.Should().BeEquivalentTo(Directory.EnumerateFiles(RepositoryKit(), "*", SearchOption.AllDirectories).Select(Path.GetFileName));
	}

	[Fact]
	public void Rewrites_a_kit_file_that_differs()
	{
		var spec = Path.Combine(EmbeddedKit.Extract(_dataDirectory, "1.2.3"), "spec.yaml");
		File.WriteAllText(spec, "stale");

		EmbeddedKit.Extract(_dataDirectory, "1.2.3");

		File.ReadAllText(spec).Should().Be(File.ReadAllText(Path.Combine(RepositoryKit(), "spec.yaml")));
	}

	[Fact]
	public void Uses_xdg_data_home_when_set()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses local application data");

		EmbeddedKit.DefaultDataDirectory(name => name == "XDG_DATA_HOME" ? "/xdg" : null, "/home/owner").Should().Be("/xdg");
		EmbeddedKit.DefaultDataDirectory(_ => null, "/home/owner").Should().Be("/home/owner/.local/share");
	}

	private static string RepositoryKit([CallerFilePath] string thisFile = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", "kits", "markeli-claude"));
}
