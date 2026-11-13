using AgentSandbox;
using AgentSandbox.Platform;
using AgentSandbox.Processes;

return await LauncherCommand.InvokeAsync(args, RunAsync, Console.Out, Console.Error, CancellationToken.None);

static async Task<int> RunAsync(LauncherOptions options, CancellationToken cancellationToken)
{
	if (!OperatingSystem.IsMacOS())
	{
		throw new LauncherException(
			"only macOS hosts are supported for now; see https://github.com/Markeli/ai-agents-tooling/issues/28");
	}

	var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
	var settings = new LauncherSettings(
		SshSocket: GetEnvironmentVariable("AGENT_SSH_SOCK") ?? Path.Combine(home, ".ssh", "markeli-agent.sock"),
		SshKey: GetEnvironmentVariable("AGENT_SSH_KEY") ?? Path.Combine(home, ".ssh", "markeli_agent"),
		ResolveKit: async ct => GetEnvironmentVariable("AGENT_SANDBOX_KIT")
			?? await EmbeddedKits.ExtractAsync(
				EmbeddedKits.Default, EmbeddedKits.DefaultDataDirectory(GetEnvironmentVariable, home), EmbeddedKits.ToolVersion, ct));

	var runner = new ProcessRunner();
	var launcher = new Launcher(runner, new MacKeychainSecretStore(runner), new MacBotSshAgent(runner), Console.Out);
	return await launcher.RunAsync(options, settings, cancellationToken);
}

// Empty counts as unset, like ${VAR:-default} in the bash launcher this replaces
static string? GetEnvironmentVariable(string name) =>
	Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
