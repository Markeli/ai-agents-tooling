using AgentSandbox;
using AgentSandbox.Mcp;
using AgentSandbox.Platform;
using AgentSandbox.Processes;

return await LauncherCommand.InvokeAsync(args, RunAsync, SetupGrafanaAsync, Console.Out, Console.Error, CancellationToken.None);

static async Task<int> RunAsync(LauncherOptions options, CancellationToken cancellationToken)
{
	EnsureMacOS();

	var home = Home();
	var settings = new LauncherSettings(
		SshSocket: GetEnvironmentVariable("AGENT_SSH_SOCK") ?? Path.Combine(home, ".ssh", "markeli-agent.sock"),
		SshKey: GetEnvironmentVariable("AGENT_SSH_KEY") ?? Path.Combine(home, ".ssh", "markeli_agent"),
		Image: GetEnvironmentVariable("AGENT_SANDBOX_IMAGE") ?? SandboxImage.Default,
		ResolveKit: async ct => GetEnvironmentVariable("AGENT_SANDBOX_KIT")
			?? await EmbeddedKits.ExtractAsync(
				EmbeddedKits.Default, EmbeddedKits.DefaultDataDirectory(GetEnvironmentVariable, home), EmbeddedKits.ToolVersion, ct));

	var runner = new ProcessRunner();
	var launcher = new Launcher(runner, new MacKeychainSecretStore(runner), new MacBotSshAgent(runner), Console.Out);
	return await launcher.RunAsync(options, settings, cancellationToken);
}

static async Task<int> SetupGrafanaAsync(GrafanaSetupOptions options, CancellationToken cancellationToken)
{
	EnsureMacOS();

	var home = Home();
	var settings = new McpSetupSettings(
		Home: home,
		DataDirectory: EmbeddedKits.DefaultDataDirectory(GetEnvironmentVariable, home),
		ToolVersion: EmbeddedKits.ToolVersion,
		SearchPath: Environment.GetEnvironmentVariable("PATH"),
		ExtraProgramDirectories: McpSetupSettings.HomebrewDirectories,
		OwnerAccount: Environment.UserName);

	var runner = new ProcessRunner();
	return await new GrafanaMcpSetup(runner, new MacKeychainSecretStore(runner), Console.Out)
		.RunAsync(options, settings, cancellationToken);
}

static void EnsureMacOS()
{
	if (!OperatingSystem.IsMacOS())
	{
		throw new LauncherException(
			"only macOS hosts are supported for now; see https://github.com/Markeli/ai-agents-tooling/issues/28");
	}
}

static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

// Empty counts as unset, like ${VAR:-default} in the bash launcher this replaces
static string? GetEnvironmentVariable(string name) =>
	Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
