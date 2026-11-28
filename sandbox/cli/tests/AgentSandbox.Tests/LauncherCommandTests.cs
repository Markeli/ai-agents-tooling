using AgentSandbox.Mcp;

namespace AgentSandbox.Tests;

public sealed class LauncherCommandTests
{
	private readonly StringWriter _output = new();
	private readonly StringWriter _error = new();
	private LauncherOptions? _options;
	private GrafanaSetupOptions? _setup;

	[Fact]
	public async Task Defaults_to_the_current_directory_and_no_flags()
	{
		(await InvokeAsync()).Should().Be(0);

		_options.Should().BeEquivalentTo(new LauncherOptions());
	}

	[Fact]
	public async Task Parses_every_flag_and_passes_everything_after_the_separator_through()
	{
		(await InvokeAsync("--no-attach", "--recreate", "--yes", "--force-pull", "--name", "blog-1", "~/repo", "--", "-c", "--", "--name", "x"))
			.Should().Be(0);

		_options.Should().BeEquivalentTo(new LauncherOptions
		{
			RepoPath = "~/repo",
			Name = "blog-1",
			NoAttach = true,
			Recreate = true,
			Yes = true,
			ForcePull = true,
			AgentArguments = ["-c", "--", "--name", "x"],
		});
	}

	[Fact]
	public async Task Keeps_arguments_after_the_separator_out_of_the_repository_path()
	{
		(await InvokeAsync("--", "--resume", "x")).Should().Be(0);

		_options.Should().BeEquivalentTo(new LauncherOptions { AgentArguments = ["--resume", "x"] });
	}

	[Fact]
	public async Task Takes_a_repository_path_that_starts_with_an_at_sign_literally()
	{
		(await InvokeAsync("@repo")).Should().Be(0);

		_options!.RepoPath.Should().Be("@repo");
	}

	[Theory]
	[InlineData("false")]
	[InlineData("true")]
	public async Task Takes_a_repository_named_like_a_boolean_after_a_flag_as_the_path(string repo)
	{
		(await InvokeAsync("--recreate", "--yes", repo)).Should().Be(0);

		_options.Should().BeEquivalentTo(new LauncherOptions { RepoPath = repo, Recreate = true, Yes = true });
	}

	[Fact]
	public async Task Help_command_prints_the_same_help_as_the_option()
	{
		(await InvokeAsync("--help")).Should().Be(0);
		var optionHelp = _output.ToString();
		_output.GetStringBuilder().Clear();

		(await InvokeAsync("help")).Should().Be(0);

		// No heading such as "Usage:" here: System.CommandLine translates those to the machine's language
		_output.ToString().Should().Be(optionHelp).And.Contain("REPO_PATH").And.Contain("--recreate").And.Contain("help");
		_options.Should().BeNull();
	}

	[Fact]
	public async Task Takes_a_directory_named_help_written_as_a_path()
	{
		(await InvokeAsync("./help")).Should().Be(0);

		_options!.RepoPath.Should().Be("./help");
	}

	[Fact]
	public async Task Rejects_an_empty_name()
	{
		(await InvokeAsync("--name", "")).Should().Be(1);

		_error.ToString().Should().Be("agent-sandbox: --name must not be empty; omit --name to use the repo-derived name\n");
		_options.Should().BeNull();
	}

	[Theory]
	[InlineData("--name")]
	[InlineData("--name", "--recreate")]
	[InlineData("repo", "second")]
	[InlineData("--unknown")]
	public async Task Rejects_malformed_command_lines_without_running(params string[] args)
	{
		(await InvokeAsync(args)).Should().NotBe(0);

		_error.ToString().Should().NotBeEmpty();
		_options.Should().BeNull();
	}

	[Theory]
	[InlineData("--name needs a value, got the option '--recreate'", "--name", "--recreate")]
	[InlineData("unknown option '--unknown' (write ./--unknown for a path)", "--unknown")]
	public async Task Explains_an_option_taken_as_a_value(string message, params string[] args)
	{
		(await InvokeAsync(args)).Should().NotBe(0);

		_error.ToString().Should().Contain(message);
	}

	[Fact]
	public async Task Prints_a_launcher_error_with_the_tool_prefix_and_returns_its_exit_code()
	{
		var exitCode = await LauncherCommand.InvokeAsync(
			[], (_, _) => throw new LauncherException("cannot start sandbox-app", 3), NoSetup, _output, _error, CancellationToken.None);

		exitCode.Should().Be(3);
		_error.ToString().Should().Be("agent-sandbox: cannot start sandbox-app\n");
	}

	[Fact]
	public async Task Prints_nothing_for_an_error_another_program_already_reported()
	{
		var exitCode = await LauncherCommand.InvokeAsync(
			[], (_, _) => throw LauncherException.ExitWith(128), NoSetup, _output, _error, CancellationToken.None);

		exitCode.Should().Be(128);
		_error.ToString().Should().BeEmpty();
	}

	[Theory]
	[MemberData(nameof(FileSystemErrors))]
	public async Task Prints_a_file_system_error_as_a_message_instead_of_a_stack_trace(Exception exception)
	{
		var exitCode = await LauncherCommand.InvokeAsync([], (_, _) => throw exception, NoSetup, _output, _error, CancellationToken.None);

		exitCode.Should().Be(1);
		_error.ToString().Should().Be($"agent-sandbox: {exception.Message}\n");
	}

	public static TheoryData<Exception> FileSystemErrors() =>
	[
		new UnauthorizedAccessException("Access to the path '/data/agent-sandbox' is denied."),
		new IOException("No space left on device"),
		new DirectoryNotFoundException("Could not find a part of the path '/missing/agent.sock'."),
	];

	[Fact]
	public async Task Parses_mcp_setup_grafana_with_its_defaults()
	{
		(await InvokeAsync("mcp", "setup", "grafana", "--url", "https://grafana.example.com")).Should().Be(0);

		_setup.Should().BeEquivalentTo(new GrafanaSetupOptions
		{
			Url = "https://grafana.example.com",
			Name = "personal-grafana",
			AgentItem = "agent/personal-grafana",
			OwnerItem = null,
		});
		_options.Should().BeNull();
	}

	[Fact]
	public async Task Parses_every_option_of_mcp_setup_grafana()
	{
		(await InvokeAsync(
			"mcp", "setup", "grafana", "--url", "https://grafana.example.com", "--name", "grafana-2",
			"--agent-item", "agent/grafana-2", "--owner-item", "owner/grafana-2"))
			.Should().Be(0);

		_setup.Should().BeEquivalentTo(new GrafanaSetupOptions
		{
			Url = "https://grafana.example.com",
			Name = "grafana-2",
			AgentItem = "agent/grafana-2",
			OwnerItem = "owner/grafana-2",
		});
	}

	[Theory]
	[InlineData("mcp", "setup", "grafana")]
	[InlineData("mcp", "setup", "grafana", "--url")]
	[InlineData("mcp", "setup", "grafana", "--url", "--name", "x")]
	[InlineData("mcp", "setup", "grafana", "--url", "https://grafana.example.com", "--owner-item", "--name")]
	[InlineData("mcp", "setup", "grafana", "--url", "https://grafana.example.com", "extra")]
	[InlineData("mcp", "setup", "other", "--url", "https://grafana.example.com")]
	public async Task Rejects_malformed_mcp_setup_command_lines_without_running(params string[] args)
	{
		(await InvokeAsync(args)).Should().NotBe(0);

		_error.ToString().Should().NotBeEmpty();
		_setup.Should().BeNull();
		_options.Should().BeNull();
	}

	[Fact]
	public async Task Rejects_agent_arguments_for_mcp_setup()
	{
		(await InvokeAsync("mcp", "setup", "grafana", "--url", "https://grafana.example.com", "--", "-c")).Should().Be(1);

		_error.ToString().Should().Be("agent-sandbox: arguments after -- are only for the agent, not for mcp setup\n");
		_setup.Should().BeNull();
	}

	[Fact]
	public async Task Takes_a_directory_named_mcp_written_as_a_path()
	{
		(await InvokeAsync("./mcp")).Should().Be(0);

		_options!.RepoPath.Should().Be("./mcp");
		_setup.Should().BeNull();
	}

	[Fact]
	public async Task Help_lists_the_mcp_command()
	{
		(await InvokeAsync("help")).Should().Be(0);

		_output.ToString().Should().Contain("mcp").And.Contain("./mcp");
	}

	private static Task<int> NoSetup(GrafanaSetupOptions options, CancellationToken cancellationToken) =>
		throw new InvalidOperationException("mcp setup must not run");

	private Task<int> InvokeAsync(params string[] args) =>
		LauncherCommand.InvokeAsync(
			args,
			(options, _) =>
			{
				_options = options;
				return Task.FromResult(0);
			},
			(setup, _) =>
			{
				_setup = setup;
				return Task.FromResult(0);
			},
			_output,
			_error,
			CancellationToken.None);
}
