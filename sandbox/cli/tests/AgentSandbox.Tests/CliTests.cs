namespace AgentSandbox.Tests;

public sealed class CliTests
{
	private readonly StringWriter _output = new();
	private readonly StringWriter _error = new();
	private LauncherOptions? _options;

	[Fact]
	public async Task Defaults_to_the_current_directory_and_no_flags()
	{
		(await InvokeAsync()).Should().Be(0);

		_options.Should().BeEquivalentTo(new LauncherOptions());
	}

	[Fact]
	public async Task Parses_every_flag_and_passes_everything_after_the_separator_through()
	{
		(await InvokeAsync("--no-attach", "--recreate", "--yes", "--name", "blog-1", "~/repo", "--", "-c", "--", "--name", "x"))
			.Should().Be(0);

		_options.Should().BeEquivalentTo(new LauncherOptions
		{
			RepoPath = "~/repo",
			Name = "blog-1",
			NoAttach = true,
			Recreate = true,
			Yes = true,
			AgentArguments = ["-c", "--", "--name", "x"],
		});
	}

	[Fact]
	public async Task Takes_a_repository_path_that_starts_with_an_at_sign_literally()
	{
		(await InvokeAsync("@repo")).Should().Be(0);

		_options!.RepoPath.Should().Be("@repo");
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
		var exitCode = await Cli.InvokeAsync(
			[], (_, _) => throw new LauncherException("cannot start sandbox-app", 3), _output, _error, CancellationToken.None);

		exitCode.Should().Be(3);
		_error.ToString().Should().Be("agent-sandbox: cannot start sandbox-app\n");
	}

	[Fact]
	public async Task Prints_nothing_for_an_error_another_program_already_reported()
	{
		var exitCode = await Cli.InvokeAsync(
			[], (_, _) => throw LauncherException.ExitWith(128), _output, _error, CancellationToken.None);

		exitCode.Should().Be(128);
		_error.ToString().Should().BeEmpty();
	}

	private Task<int> InvokeAsync(params string[] args) =>
		Cli.InvokeAsync(
			args,
			(options, _) =>
			{
				_options = options;
				return Task.FromResult(0);
			},
			_output,
			_error,
			CancellationToken.None);
}
