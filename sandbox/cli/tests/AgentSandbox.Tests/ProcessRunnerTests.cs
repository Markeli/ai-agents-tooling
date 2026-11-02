using AgentSandbox.Processes;

namespace AgentSandbox.Tests;

public sealed class ProcessRunnerTests
{
	private readonly ProcessRunner _runner = new();

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
}
