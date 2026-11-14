using AgentSandbox.Processes;

namespace AgentSandbox.Tests;

public sealed class ProcessRunnerTests : IDisposable
{
	private readonly ProcessRunner _runner = new();
	private readonly string _directory = Directory.CreateTempSubdirectory("agent-sandbox-tests-").FullName;

	public void Dispose() => Directory.Delete(_directory, recursive: true);

	[Fact]
	public async Task Captures_both_streams_and_the_exit_code()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "needs /bin/sh");

		var result = await _runner.RunAsync(
			new ProcessSpec("/bin/sh", ["-c", "echo out; echo err >&2; exit 3"]),
			TestContext.Current.CancellationToken);

		result.Should().Be(new ProcessResult(3, "out\n", "err\n"));
	}

	[Fact]
	public async Task Passes_extra_environment_variables()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "needs /bin/sh");

		var result = await _runner.RunAsync(
			new ProcessSpec("/bin/sh", ["-c", "printf %s \"$SSH_AUTH_SOCK\""])
			{
				Environment = new Dictionary<string, string> { ["SSH_AUTH_SOCK"] = "/tmp/agent.sock" },
			},
			TestContext.Current.CancellationToken);

		result.StdOut.Should().Be("/tmp/agent.sock");
	}

	[Fact]
	public async Task Passes_arguments_without_shell_splitting()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "needs /bin/sh");

		var result = await _runner.RunAsync(
			new ProcessSpec("/bin/sh", ["-c", "printf '%s|' \"$@\"", "sh", "a b", "$HOME", ""]),
			TestContext.Current.CancellationToken);

		result.StdOut.Should().Be("a b|$HOME||");
	}

	[Fact]
	public async Task Reports_a_missing_program_like_a_shell()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "looks programs up like a Unix shell");

		var result = await _runner.RunAsync(
			new ProcessSpec("agent-sandbox-no-such-program", []), TestContext.Current.CancellationToken);

		result.Should().Be(new ProcessResult(127, StdErr: "agent-sandbox-no-such-program: command not found"));
	}

	[Fact]
	public async Task Returns_the_exit_code_of_an_interactive_program()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "needs /bin/sh");

		(await _runner.RunInteractiveAsync(new ProcessSpec("/bin/sh", ["-c", "exit 5"]), TestContext.Current.CancellationToken))
			.Should().Be(5);
	}

	[Fact]
	public async Task Runs_the_first_executable_of_that_name_in_the_search_path()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "looks programs up like a Unix shell");
		var plain = Directory.CreateDirectory(Path.Combine(_directory, "plain")).FullName;
		var bin = Directory.CreateDirectory(Path.Combine(_directory, "bin")).FullName;
		File.WriteAllText(Path.Combine(plain, "tool"), "#!/bin/sh\necho plain\n");
		Script(Path.Combine(bin, "tool"), "echo bin");

		var result = await new ProcessRunner($"{plain}{Path.PathSeparator}{bin}")
			.RunAsync(new ProcessSpec("tool", []), TestContext.Current.CancellationToken);

		result.Should().Be(new ProcessResult(0, "bin\n"));
	}

	[Fact]
	public void Skips_empty_search_path_entries_that_a_shell_reads_as_the_current_directory()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "looks programs up like a Unix shell");

		ProcessRunner.Resolve("ls", $"{Path.PathSeparator}{_directory}{Path.PathSeparator}").Should().BeNull();
		ProcessRunner.Resolve("./ls", _directory).Should().Be("./ls", "a name with a slash is a path");
	}

	[Fact]
	public async Task Reports_why_a_program_cannot_run()
	{
		Assert.SkipWhen(OperatingSystem.IsWindows(), "needs Unix file modes");
		var program = Path.Combine(_directory, "not-executable");
		File.WriteAllText(program, string.Empty);

		var result = await _runner.RunAsync(new ProcessSpec(program, []), TestContext.Current.CancellationToken);

		result.Should().Be(new ProcessResult(126, StdErr: $"{program}: Permission denied"));
	}

	private static void Script(string path, string body)
	{
		File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
		// The callers skip on Windows, which the analyzer can't see through Assert.SkipWhen
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}
}
