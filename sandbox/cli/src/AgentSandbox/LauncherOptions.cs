namespace AgentSandbox;

/// <summary>What the command line asks for.</summary>
internal sealed record LauncherOptions
{
	public string RepoPath { get; init; } = ".";

	/// <summary>The sandbox name; <see langword="null"/> derives it from the repository directory.</summary>
	public string? Name { get; init; }

	public bool NoAttach { get; init; }

	public bool Recreate { get; init; }

	public bool Yes { get; init; }

	/// <summary>Arguments after <c>--</c>, passed to the agent through <c>sbx run</c>.</summary>
	public IReadOnlyList<string> AgentArguments { get; init; } = [];
}

/// <summary>Host settings, overridable through environment variables.</summary>
/// <param name="SshSocket">The bot's ssh-agent socket (<c>AGENT_SSH_SOCK</c>).</param>
/// <param name="SshKey">The bot's private key (<c>AGENT_SSH_KEY</c>).</param>
/// <param name="Pull">The <c>sbx create --pull</c> policy (<c>AGENT_SANDBOX_PULL</c>).</param>
/// <param name="ResolveKit">Returns the kit directory (<c>AGENT_SANDBOX_KIT</c>, or an embedded kit), only when needed.</param>
internal sealed record LauncherSettings(
	string SshSocket,
	string SshKey,
	string Pull,
	Func<CancellationToken, Task<string>> ResolveKit);
