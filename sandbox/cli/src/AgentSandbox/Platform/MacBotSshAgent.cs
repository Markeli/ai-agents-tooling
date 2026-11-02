using AgentSandbox.Processes;

namespace AgentSandbox.Platform;

/// <summary>A dedicated ssh-agent on its own socket; the key's passphrase comes from the login keychain.</summary>
internal sealed class MacBotSshAgent(IProcessRunner runner) : IBotSshAgent
{
	public async Task EnsureLoadedAsync(string socket, string keyPath, CancellationToken cancellationToken)
	{
		var environment = new Dictionary<string, string> { ["SSH_AUTH_SOCK"] = socket };
		var list = await runner.RunAsync(new ProcessSpec("ssh-add", ["-l"]) { Environment = environment }, cancellationToken);
		if (list.Succeeded)
		{
			return;
		}

		// A stale socket left by an agent that died would make ssh-agent -a fail
		File.Delete(socket);
		var agent = await runner.RunAsync(
			new ProcessSpec("ssh-agent", ["-a", socket]) { StdErr = OutputMode.Inherit },
			cancellationToken);
		if (!agent.Succeeded)
		{
			throw new LauncherException($"cannot start ssh-agent on {socket}");
		}

		// The passphrase comes from the login keychain after the first interactive ssh-add --apple-use-keychain
		var add = await runner.RunAsync(
			new ProcessSpec("ssh-add", ["--apple-use-keychain", keyPath]) { Environment = environment, StdOut = OutputMode.Inherit },
			cancellationToken);
		if (!add.Succeeded)
		{
			throw new LauncherException($"cannot load {keyPath} into {socket}");
		}
	}
}
