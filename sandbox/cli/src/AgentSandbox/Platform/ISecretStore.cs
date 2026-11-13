namespace AgentSandbox.Platform;

/// <summary>The host's secret store that holds the bot's API tokens.</summary>
internal interface ISecretStore
{
	Task<bool> ContainsAsync(string item, CancellationToken cancellationToken);

	/// <summary>
	/// A command that prints the secret, for <c>sbx secret set --command</c>: sbx runs it on the host whenever it needs
	/// the token, so the token itself never passes through the launcher.
	/// </summary>
	string LookupCommand(string item);
}
