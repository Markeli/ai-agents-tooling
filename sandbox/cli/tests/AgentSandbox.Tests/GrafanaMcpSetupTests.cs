using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using AgentSandbox.Mcp;

namespace AgentSandbox.Tests;

// Unix file modes and symlinks; the setup runs on macOS, CI on Linux
[UnsupportedOSPlatform("windows")]
public sealed class GrafanaMcpSetupTests : IDisposable
{
	private const string Url = GrafanaSetupFixture.Url;

	private static readonly GrafanaSetupOptions Defaults = new() { Url = Url };
	private static readonly GrafanaSetupOptions WithOwner = Defaults with { OwnerItem = "owner/personal-grafana" };

	private readonly GrafanaSetupFixture _fixture = new();

	public void Dispose() => _fixture.Dispose();

	[Fact]
	public async Task Writes_the_scripts_links_them_and_registers_the_server_in_sbx()
	{
		(await _fixture.RunAsync(Defaults)).Should().Be(0);

		foreach (var script in new[] { "keychain-env", "grafana-mcp" })
		{
			var installed = Path.Combine(_fixture.Scripts, script);
			File.ReadAllBytes(installed).Should().Equal(File.ReadAllBytes(RepositoryScript(script)));
			File.GetUnixFileMode(installed).Should().Be((UnixFileMode)0b111_101_101, "0755");
			new FileInfo(Path.Combine(_fixture.Bin, script)).LinkTarget.Should().Be(installed);
		}

		_fixture.Runner.CommandLines.Should().Equal(
			"sbx mcp ls --quiet",
			$"sbx mcp add personal-grafana --command {_fixture.Command} --args {Url},agent/personal-grafana");
		_fixture.SbxServers.Should().ContainKey("personal-grafana")
			.WhoseValue.Arguments.Should().Equal(Url, "agent/personal-grafana");
		_fixture.Output.ToString().Should()
			.Contain($"agent-sandbox: scripts: wrote {_fixture.Scripts}\n")
			.And.Contain($"agent-sandbox: scripts: linked {_fixture.Command} -> {Path.Combine(_fixture.Scripts, "grafana-mcp")}\n")
			.And.Contain($"agent-sandbox: mcp-grafana: found {Path.Combine(_fixture.PathDirectory, "mcp-grafana")}\n")
			.And.Contain("agent-sandbox: keychain: agent/personal-grafana found\n")
			.And.Contain("agent-sandbox: sbx: registered personal-grafana\n")
			.And.Contain("agent-sandbox: claude code: skipped (no --owner-item)\n")
			.And.EndWith("agent-sandbox: personal-grafana is set up\n");
	}

	[Fact]
	public async Task With_an_owner_item_registers_the_server_in_claude_code_too()
	{
		(await _fixture.RunAsync(WithOwner)).Should().Be(0);

		_fixture.Runner.CommandLines.Should().Equal(
			"sbx mcp ls --quiet",
			$"sbx mcp add personal-grafana --command {_fixture.Command} --args {Url},agent/personal-grafana",
			"claude mcp get personal-grafana",
			$"claude mcp add --scope user personal-grafana -- {_fixture.Command} {Url} owner/personal-grafana");
		_fixture.Runner.Calls[3].Spec.Arguments.Should().Equal(
			"mcp", "add", "--scope", "user", "personal-grafana", "--", _fixture.Command, Url, "owner/personal-grafana");
		_fixture.Output.ToString().Should().Contain("agent-sandbox: claude code: registered personal-grafana (user scope)\n");
	}

	[Fact]
	public async Task The_second_run_changes_nothing_and_reports_every_step_as_done()
	{
		await _fixture.RunAsync(WithOwner);
		var scripts = Directory.GetFiles(_fixture.Scripts).ToDictionary(f => f, File.GetLastWriteTimeUtc);

		(await _fixture.RunAgainAsync(WithOwner)).Should().Be(0);

		_fixture.Runner.CommandLines.Should().Equal(
			"sbx mcp ls --quiet",
			"sbx mcp inspect personal-grafana --json",
			"claude mcp get personal-grafana");
		Directory.GetFiles(_fixture.Scripts).ToDictionary(f => f, File.GetLastWriteTimeUtc).Should().Equal(scripts);
		_fixture.Output.ToString().Should()
			.Contain($"agent-sandbox: scripts: {_fixture.Scripts} up to date\n")
			.And.Contain($"agent-sandbox: scripts: {_fixture.Command} already links to {Path.Combine(_fixture.Scripts, "grafana-mcp")}\n")
			.And.Contain("agent-sandbox: sbx: personal-grafana already registered\n")
			.And.Contain("agent-sandbox: claude code: personal-grafana already registered (user scope)\n")
			.And.NotContain("wrote")
			.And.NotContain(" linked ")
			.And.NotContain("re-registered");
	}

	[Fact]
	public async Task Replaces_registrations_with_other_arguments()
	{
		_fixture.SbxServers["personal-grafana"] = (_fixture.Command, ["https://old.example.com", "agent/personal-grafana"]);
		_fixture.ClaudeServers["personal-grafana"] =
			("User config (available in all your projects)", "/usr/local/bin/grafana-mcp", [Url, "owner/personal-grafana"]);

		(await _fixture.RunAsync(WithOwner)).Should().Be(0);

		_fixture.Runner.CommandLines.Should().Equal(
			"sbx mcp ls --quiet",
			"sbx mcp inspect personal-grafana --json",
			"sbx mcp rm --force personal-grafana",
			$"sbx mcp add personal-grafana --command {_fixture.Command} --args {Url},agent/personal-grafana",
			"claude mcp get personal-grafana",
			"claude mcp remove --scope user personal-grafana",
			$"claude mcp add --scope user personal-grafana -- {_fixture.Command} {Url} owner/personal-grafana");
		_fixture.SbxServers["personal-grafana"].Arguments.Should().Equal(Url, "agent/personal-grafana");
		_fixture.ClaudeServers["personal-grafana"].Command.Should().Be(_fixture.Command);
		_fixture.Output.ToString().Should()
			.Contain("agent-sandbox: sbx: re-registered personal-grafana (its command or arguments differed)\n")
			.And.Contain("agent-sandbox: claude code: re-registered personal-grafana (its command or arguments differed)\n");
	}

	[Fact]
	public async Task Re_registers_in_sbx_when_inspect_output_cannot_confirm_the_arguments()
	{
		_fixture.SbxServers["personal-grafana"] = (_fixture.Command, [Url, "agent/personal-grafana"]);
		_fixture.SbxInspectJson = (_, _, _) => "NAME  COMMAND\npersonal-grafana  something\n";

		(await _fixture.RunAsync(Defaults)).Should().Be(0);

		_fixture.Runner.CommandLines.Should().Contain("sbx mcp rm --force personal-grafana");
		_fixture.SbxServers["personal-grafana"].Arguments.Should().Equal(Url, "agent/personal-grafana");
	}

	[Fact]
	public async Task Leaves_a_file_at_a_link_path_alone_and_fails_after_the_other_steps()
	{
		Directory.CreateDirectory(_fixture.Bin);
		var foreign = Path.Combine(_fixture.Bin, "keychain-env");
		File.WriteAllText(foreign, "mine");

		var run = () => _fixture.RunAsync(Defaults);

		(await run.Should().ThrowAsync<LauncherException>())
			.Which.Message.Should().Be($"personal-grafana is not set up yet: {foreign} is in the way");
		File.ReadAllText(foreign).Should().Be("mine");
		new FileInfo(foreign).LinkTarget.Should().BeNull();
		new FileInfo(_fixture.Command).LinkTarget.Should().Be(Path.Combine(_fixture.Scripts, "grafana-mcp"));
		_fixture.SbxServers.Should().ContainKey("personal-grafana");
		_fixture.Output.ToString().Should().Contain($"agent-sandbox: scripts: {foreign} exists and is not a link into agent-sandbox/mcp/");
	}

	[Fact]
	public async Task Leaves_a_link_pointing_elsewhere_alone()
	{
		Directory.CreateDirectory(_fixture.Bin);
		File.CreateSymbolicLink(_fixture.Command, "/opt/grafana/grafana-mcp");

		var run = () => _fixture.RunAsync(Defaults);

		await run.Should().ThrowAsync<LauncherException>().WithMessage("*grafana-mcp is in the way");
		new FileInfo(_fixture.Command).LinkTarget.Should().Be("/opt/grafana/grafana-mcp");
	}

	[Fact]
	public async Task Repoints_the_links_of_an_older_version_and_removes_its_scripts()
	{
		var old = Path.Combine(_fixture.Data, "agent-sandbox", "mcp", "1.2.2");
		Directory.CreateDirectory(old);
		Directory.CreateDirectory(_fixture.Bin);
		foreach (var script in new[] { "keychain-env", "grafana-mcp" })
		{
			File.WriteAllText(Path.Combine(old, script), "old");
			File.CreateSymbolicLink(Path.Combine(_fixture.Bin, script), Path.Combine(old, script));
		}

		(await _fixture.RunAsync(Defaults)).Should().Be(0);

		new FileInfo(_fixture.Command).LinkTarget.Should().Be(Path.Combine(_fixture.Scripts, "grafana-mcp"));
		new FileInfo(Path.Combine(_fixture.Bin, "keychain-env")).LinkTarget.Should().Be(Path.Combine(_fixture.Scripts, "keychain-env"));
		Directory.Exists(old).Should().BeFalse();
		Directory.GetFiles(_fixture.Bin).Select(Path.GetFileName).Should().BeEquivalentTo("keychain-env", "grafana-mcp");
	}

	[Fact]
	public async Task Missing_mcp_grafana_prints_the_install_hint_and_fails_after_the_other_steps()
	{
		_fixture.RemoveProgram("mcp-grafana");

		var run = () => _fixture.RunAsync(Defaults);

		await run.Should().ThrowAsync<LauncherException>()
			.WithMessage("personal-grafana is not set up yet: mcp-grafana is not installed");
		_fixture.Output.ToString().Should().Contain("brew install mcp-grafana");
		_fixture.SbxServers.Should().ContainKey("personal-grafana");
	}

	[Fact]
	public async Task Finds_mcp_grafana_in_homebrews_directories_outside_path()
	{
		_fixture.RemoveProgram("mcp-grafana");
		var homebrew = Path.Combine(_fixture.Home, "homebrew-bin");
		Directory.CreateDirectory(homebrew);
		File.WriteAllText(Path.Combine(homebrew, "mcp-grafana"), "#!/bin/sh\n");
		File.SetUnixFileMode(Path.Combine(homebrew, "mcp-grafana"), UnixFileMode.UserRead | UnixFileMode.UserExecute);

		var exitCode = await new GrafanaMcpSetup(_fixture.Runner, _fixture.Secrets, _fixture.Output)
			.RunAsync(Defaults, _fixture.Settings with { ExtraProgramDirectories = [homebrew] }, CancellationToken.None);

		exitCode.Should().Be(0);
		_fixture.Output.ToString().Should().Contain($"agent-sandbox: mcp-grafana: found {Path.Combine(homebrew, "mcp-grafana")}\n");
	}

	[Fact]
	public async Task Missing_items_print_how_to_create_them()
	{
		_fixture.Secrets.Items.Clear();

		var run = () => _fixture.RunAsync(WithOwner);

		await run.Should().ThrowAsync<LauncherException>().WithMessage(
			"personal-grafana is not set up yet: keychain item agent/personal-grafana is missing; "
			+ "keychain item owner/personal-grafana is missing");
		var output = _fixture.Output.ToString();
		output.Should()
			.Contain("agent-sandbox: keychain: agent/personal-grafana (the bot's token, for the sandboxes) not found; create it:")
			.And.Contain("Administration → Users and access → Service accounts → Add service account")
			.And.Contain("role Editor → Add service account token (expiry: one year)")
			.And.Contain("security add-generic-password -U -s agent/personal-grafana -a markeli-agent -w\n")
			.And.Contain("security add-generic-password -U -s owner/personal-grafana -a owner -w\n")
			.And.Contain("\"Always Allow\"");
	}

	[Fact]
	public async Task Without_an_owner_item_only_the_bots_item_is_required()
	{
		_fixture.Secrets.Items.Remove("owner/personal-grafana");

		(await _fixture.RunAsync(Defaults)).Should().Be(0);
	}

	[Fact]
	public async Task Skips_claude_code_when_claude_is_not_in_path()
	{
		_fixture.RemoveProgram("claude");

		(await _fixture.RunAsync(WithOwner)).Should().Be(0);

		_fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("claude"));
		_fixture.Output.ToString().Should().Contain(
			"agent-sandbox: claude code: claude is not in PATH, skipped; to register it later: "
			+ $"claude mcp add --scope user personal-grafana -- {_fixture.Command} {Url} owner/personal-grafana\n");
	}

	[Fact]
	public async Task Leaves_a_claude_code_server_of_another_scope_alone()
	{
		_fixture.ClaudeServers["personal-grafana"] = ("Local config (private to you in this project)", "/x", ["y"]);

		var run = () => _fixture.RunAsync(WithOwner);

		await run.Should().ThrowAsync<LauncherException>().WithMessage("*personal-grafana in another Claude Code scope");
		_fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("claude mcp add") || c.StartsWith("claude mcp remove"));
	}

	[Fact]
	public async Task Prints_what_is_left_to_do_by_hand()
	{
		await _fixture.RunAsync(WithOwner);

		_fixture.Output.ToString().Should()
			.Contain("claude_desktop_config.json")
			.And.Contain(
				$"    {{\n      \"personal-grafana\": {{\n        \"command\": \"{_fixture.Command}\",\n        \"args\": [\n"
				+ $"          \"{Url}\",\n          \"owner/personal-grafana\"\n        ]\n      }}\n    }}\n")
			.And.Contain("GF_SNAPSHOTS_EXTERNAL_ENABLED=false")
			.And.Contain("GF_PUBLIC_DASHBOARDS_ENABLED=false")
			.And.Contain("mcp-find")
			.And.Contain("mcp-add")
			.And.Contain("External dashboard creation is disabled");
	}

	[Fact]
	public async Task Reports_a_failing_sbx_with_its_error()
	{
		_fixture.Runner.On("sbx mcp ls --quiet", 127, string.Empty);

		var run = () => _fixture.RunAsync(Defaults);

		await run.Should().ThrowAsync<LauncherException>().WithMessage("cannot list the sbx MCP servers (exit 127)*");
	}

	[Theory]
	[InlineData("grafana.example.com")]
	[InlineData("ftp://grafana.example.com")]
	[InlineData("https://grafana.example.com/a,b")]
	[InlineData("https://grafana.example.com/a b")]
	public async Task Rejects_an_url_that_is_not_plain_http(string url)
	{
		var run = () => _fixture.RunAsync(new GrafanaSetupOptions { Url = url });

		await run.Should().ThrowAsync<LauncherException>().WithMessage("--url must be an http(s) URL*");
		_fixture.Runner.Calls.Should().BeEmpty();
		Directory.Exists(_fixture.Data).Should().BeFalse();
	}

	[Theory]
	[InlineData("bad,name", "agent/personal-grafana", null)]
	[InlineData("-name", "agent/personal-grafana", null)]
	[InlineData("personal-grafana", "agent/a,b", null)]
	[InlineData("personal-grafana", "agent/personal-grafana", "owner item")]
	public async Task Rejects_names_and_items_that_do_not_fit_into_the_registrations(string name, string agentItem, string? ownerItem)
	{
		var run = () => _fixture.RunAsync(new GrafanaSetupOptions { Url = Url, Name = name, AgentItem = agentItem, OwnerItem = ownerItem });

		await run.Should().ThrowAsync<LauncherException>().WithMessage("invalid --*");
		_fixture.Runner.Calls.Should().BeEmpty();
	}

	[Theory]
	[InlineData("""{"name":"g","command":"/c","args":["u","i"]}""", true)]
	[InlineData("""{"Name":"g","Command":"/c","Args":"u,i"}""", true)]
	[InlineData("""{"registration":{"server":{"type":"stdio","command":"/c","args":["u","i"]}}}""", true)]
	[InlineData("""[{"name":"g","spec":{"command":["/c","u","i"]}}]""", true)]
	[InlineData("""{"name":"g","command":"/c","args":["u","other"]}""", false)]
	[InlineData("""{"name":"g","command":"/other","args":["u","i"]}""", false)]
	[InlineData("""{"name":"g","command":"/c"}""", false)]
	[InlineData("""{"name":"g","command":"/c","args":["u",1]}""", false)]
	[InlineData("NAME COMMAND\ng /c", false)]
	[InlineData("", false)]
	public void Recognises_a_registration_in_sbx_inspect_json(string json, bool expected)
	{
		GrafanaMcpSetup.IsSbxRegistration(json, "/c", ["u", "i"]).Should().Be(expected);
	}

	[Fact]
	public void Parses_claude_mcp_get()
	{
		var server = GrafanaMcpSetup.ParseClaudeServer(
			"personal-grafana:\n  Scope: User config (available in all your projects)\n  Status: ✘ Failed to connect\n"
			+ "  Type: stdio\n  Command: /Users/o/.local/bin/grafana-mcp\n  Args: https://grafana.example.com owner/g\n"
			+ "  Environment:\n\nTo remove this server, run: claude mcp remove personal-grafana -s user\n");

		server["Scope"].Should().Be("User config (available in all your projects)");
		server["Command"].Should().Be("/Users/o/.local/bin/grafana-mcp");
		server["Args"].Should().Be("https://grafana.example.com owner/g");
	}

	private static string RepositoryScript(string name, [CallerFilePath] string thisFile = "") =>
		Path.GetFullPath(Path.Combine(
			Path.GetDirectoryName(thisFile)!, "..", "..", "..", "..", "plugins", "mcp-servers",
			name == "grafana-mcp" ? "grafana" : name, name));
}
