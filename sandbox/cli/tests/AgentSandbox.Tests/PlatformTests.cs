using AgentSandbox.Platform;
using AgentSandbox.Processes;

namespace AgentSandbox.Tests;

public sealed class MacBotSshAgentTests : IDisposable
{
	private const string Key = "/home/owner/.ssh/markeli_agent";
	private readonly string _directory = Directory.CreateTempSubdirectory("agent-sandbox-tests-").FullName;
	private readonly FakeProcessRunner _runner = new();

	public MacBotSshAgentTests()
	{
		Socket = Path.Combine(_directory, "agent.sock");
	}

	private string Socket { get; }

	public void Dispose() => Directory.Delete(_directory, recursive: true);

	[Fact]
	public async Task Leaves_a_running_agent_with_keys_alone()
	{
		_runner.On("ssh-add -l", 0);

		await new MacBotSshAgent(_runner).EnsureLoadedAsync(Socket, Key, CancellationToken.None);

		_runner.CommandLines.Should().Equal("ssh-add -l");
		_runner.Calls.Single().Spec.Environment.Should().Contain("SSH_AUTH_SOCK", Socket);
	}

	[Fact]
	public async Task Replaces_a_stale_socket_with_a_new_agent_and_loads_the_key()
	{
		File.WriteAllText(Socket, "stale");
		_runner
			.On("ssh-add -l", 2)
			.On($"ssh-agent -a {Socket}", spec =>
			{
				File.Exists(Socket).Should().BeFalse("a stale socket makes ssh-agent -a fail");
				return new ProcessResult(0);
			})
			.On($"ssh-add --apple-use-keychain {Key}", 0);

		await new MacBotSshAgent(_runner).EnsureLoadedAsync(Socket, Key, CancellationToken.None);

		_runner.CommandLines.Should().Equal("ssh-add -l", $"ssh-agent -a {Socket}", $"ssh-add --apple-use-keychain {Key}");
		_runner.Calls[2].Spec.Environment.Should().Contain("SSH_AUTH_SOCK", Socket);
	}

	[Fact]
	public async Task Leaves_a_socket_in_a_missing_directory_to_ssh_agent()
	{
		var socket = Path.Combine(_directory, "missing", "agent.sock");
		_runner.On("ssh-add -l", 2).On($"ssh-agent -a {socket}", 1);

		var act = () => new MacBotSshAgent(_runner).EnsureLoadedAsync(socket, Key, CancellationToken.None);

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be($"cannot start ssh-agent on {socket}");
	}

	[Fact]
	public async Task Stops_when_the_key_cannot_be_loaded()
	{
		_runner.On("ssh-add -l", 1).On($"ssh-agent -a {Socket}", 0).On($"ssh-add --apple-use-keychain {Key}", 1);

		var act = () => new MacBotSshAgent(_runner).EnsureLoadedAsync(Socket, Key, CancellationToken.None);

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be($"cannot load {Key} into {Socket}");
	}

	[Fact]
	public async Task Stops_when_the_agent_cannot_start()
	{
		_runner.On("ssh-add -l", 1).On($"ssh-agent -a {Socket}", 1);

		var act = () => new MacBotSshAgent(_runner).EnsureLoadedAsync(Socket, Key, CancellationToken.None);

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be($"cannot start ssh-agent on {Socket}");
		_runner.CommandLines.Should().NotContain(c => c.StartsWith("ssh-add --apple-use-keychain"));
	}
}

public sealed class MacKeychainSecretStoreTests
{
	[Theory]
	[InlineData(0, true)]
	[InlineData(44, false)]
	public async Task Looks_up_a_generic_password_by_service(int exitCode, bool found)
	{
		var runner = new FakeProcessRunner().On("/usr/bin/security find-generic-password -s agent/personal-github", exitCode);

		(await new MacKeychainSecretStore(runner).ContainsAsync("agent/personal-github", CancellationToken.None))
			.Should().Be(found);
	}

	[Fact]
	public void Gives_sbx_a_command_that_prints_the_password() =>
		new MacKeychainSecretStore(new FakeProcessRunner()).LookupCommand("agent/personal-github")
			.Should().Be("/usr/bin/security find-generic-password -s agent/personal-github -w");
}
