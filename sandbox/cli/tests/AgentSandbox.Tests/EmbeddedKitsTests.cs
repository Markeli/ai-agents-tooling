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
	public async Task Deletes_files_the_kit_no_longer_has()
	{
		var kit = await ExtractAsync(EmbeddedKits.Default);
		Directory.CreateDirectory(Path.Combine(kit, "removed"));
		await File.WriteAllTextAsync(Path.Combine(kit, "removed", "network.yaml"), "stale", TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(Path.Combine(kit, "renamed.yaml"), "stale", TestContext.Current.CancellationToken);

		await ExtractAsync(EmbeddedKits.Default);

		RelativeFiles(kit).Should().BeEquivalentTo(RelativeFiles(Path.Combine(RepositoryKits(), EmbeddedKits.Default)));
	}

	[Fact]
	public async Task Removes_the_kits_of_other_tool_versions()
	{
		var kits = Path.Combine(_dataDirectory, "agent-sandbox", "kits");
		Directory.CreateDirectory(Path.Combine(kits, "1.2.2", EmbeddedKits.Default));
		await File.WriteAllTextAsync(
			Path.Combine(kits, "1.2.2", EmbeddedKits.Default, "spec.yaml"), "old", TestContext.Current.CancellationToken);

		await ExtractAsync(EmbeddedKits.Default);

		Directory.EnumerateDirectories(kits).Select(Path.GetFileName).Should().Equal("1.2.3");
	}

	[Fact]
	public async Task Launchers_started_side_by_side_write_the_same_kit()
	{
		var kits = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => ExtractAsync(EmbeddedKits.Default))));

		var kit = kits.Distinct().Should().ContainSingle().Subject;
		foreach (var file in RelativeFiles(Path.Combine(RepositoryKits(), EmbeddedKits.Default)))
		{
			File.ReadAllBytes(Path.Combine(kit, file))
				.Should().Equal(File.ReadAllBytes(Path.Combine(RepositoryKits(), EmbeddedKits.Default, file)));
		}

		Directory.EnumerateFiles(Path.GetDirectoryName(kit)!).Should().BeEmpty("temporary files are renamed into the kit");
	}

	[Fact]
	public void Names_the_kit_directory_after_the_package_version()
	{
		EmbeddedKits.ToolVersion.Should().MatchRegex(
			@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", "a +<commit> suffix would give every build its own directory");
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
		Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
			.Where(f => Path.GetFileName(f) != ".DS_Store")
			.Select(f => Path.GetRelativePath(directory, f));

	private static string RepositoryKits([CallerFilePath] string thisFile = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", "kits"));
}
