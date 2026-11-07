using System.Runtime.CompilerServices;

namespace AgentSandbox.Tests;

public sealed class EmbeddedKitsTests : IDisposable
{
	private readonly string _dataDirectory = Directory.CreateTempSubdirectory("agent-sandbox-tests-").FullName;

	public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

	[Fact]
	public void Embeds_every_kit_of_this_repository()
	{
		EmbeddedKits.Names.Should().Equal(
			Directory.EnumerateDirectories(RepositoryKits()).Select(Path.GetFileName).Order(StringComparer.Ordinal));
		EmbeddedKits.Names.Should().Contain(EmbeddedKits.Default);
	}

	[Theory]
	[MemberData(nameof(Kits))]
	public async Task Writes_a_kit_of_this_repository_under_a_directory_per_version(string name)
	{
		var kit = await ExtractAsync(name);

		kit.Should().Be(Path.Combine(_dataDirectory, "agent-sandbox", "kits", "1.2.3", name));
		RelativeFiles(kit).Should().BeEquivalentTo(RelativeFiles(Path.Combine(RepositoryKits(), name)));
		foreach (var file in RelativeFiles(kit))
		{
			File.ReadAllBytes(Path.Combine(kit, file)).Should().Equal(File.ReadAllBytes(Path.Combine(RepositoryKits(), name, file)));
		}
	}

	[Fact]
	public async Task Writes_only_the_requested_kit()
	{
		await ExtractAsync(EmbeddedKits.Default);

		Directory.EnumerateDirectories(Path.Combine(_dataDirectory, "agent-sandbox", "kits", "1.2.3"))
			.Select(Path.GetFileName).Should().Equal(EmbeddedKits.Default);
	}

	[Fact]
	public async Task Rewrites_a_kit_file_that_differs()
	{
		var spec = Path.Combine(await ExtractAsync(EmbeddedKits.Default), "spec.yaml");
		await File.WriteAllTextAsync(spec, "stale", TestContext.Current.CancellationToken);

		await ExtractAsync(EmbeddedKits.Default);

		File.ReadAllText(spec).Should().Be(File.ReadAllText(Path.Combine(RepositoryKits(), EmbeddedKits.Default, "spec.yaml")));
	}

	[Fact]
	public async Task Rejects_an_unknown_kit_and_lists_the_embedded_ones()
	{
		var extract = () => ExtractAsync("no-such-kit");

		(await extract.Should().ThrowAsync<LauncherException>())
			.WithMessage($"no kit named 'no-such-kit'; the tool has: *{EmbeddedKits.Default}*");
		Directory.Exists(Path.Combine(_dataDirectory, "agent-sandbox")).Should().BeFalse();
	}

	[Fact]
	public void Uses_xdg_data_home_when_set()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses local application data");

		EmbeddedKits.DefaultDataDirectory(name => name == "XDG_DATA_HOME" ? "/xdg" : null, "/home/owner").Should().Be("/xdg");
		EmbeddedKits.DefaultDataDirectory(_ => null, "/home/owner").Should().Be("/home/owner/.local/share");
	}

	public static TheoryData<string> Kits() => new(Directory.EnumerateDirectories(RepositoryKits()).Select(Path.GetFileName)!);

	private Task<string> ExtractAsync(string kit) =>
		EmbeddedKits.ExtractAsync(kit, _dataDirectory, "1.2.3", TestContext.Current.CancellationToken);

	private static IEnumerable<string> RelativeFiles(string directory) =>
		Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(directory, f));

	private static string RepositoryKits([CallerFilePath] string thisFile = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", "kits"));
}
