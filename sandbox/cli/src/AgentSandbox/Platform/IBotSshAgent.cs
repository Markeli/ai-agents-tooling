namespace AgentSandbox.Platform;

/// <summary>The SSH agent that holds only the bot's key and that sbx forwards into every sandbox.</summary>
internal interface IBotSshAgent
{
	/// <summary>Starts the agent on <paramref name="socket"/> and loads <paramref name="keyPath"/> unless it holds keys.</summary>
	Task EnsureLoadedAsync(string socket, string keyPath, CancellationToken cancellationToken);
}
