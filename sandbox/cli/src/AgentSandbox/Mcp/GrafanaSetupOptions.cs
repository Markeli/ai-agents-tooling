namespace AgentSandbox.Mcp;

/// <summary>What <c>agent-sandbox mcp setup grafana</c> asks for.</summary>
internal sealed record GrafanaSetupOptions
{
	public const string DefaultName = "personal-grafana";

	public const string DefaultAgentItem = "agent/personal-grafana";

	/// <summary>The Grafana base URL, e.g. <c>https://grafana.example.com</c>.</summary>
	public required string Url { get; init; }

	/// <summary>The server's name in sbx and Claude Code.</summary>
	public string Name { get; init; } = DefaultName;

	/// <summary>The Keychain item with the bot's service account token, for the sandboxes.</summary>
	public string AgentItem { get; init; } = DefaultAgentItem;

	/// <summary>
	/// The Keychain item with the owner's token, for Claude Code and Desktop; <see langword="null"/> skips them.
	/// </summary>
	public string? OwnerItem { get; init; }
}

/// <summary>The host the setup runs on.</summary>
/// <param name="Home">The user's home directory; the symlinks go to <c>~/.local/bin</c>.</param>
/// <param name="DataDirectory">The per-user data directory the scripts are written under.</param>
/// <param name="ToolVersion">The tool version, which names the scripts' directory.</param>
/// <param name="SearchPath">The user's <c>PATH</c>.</param>
/// <param name="ExtraProgramDirectories">Where else <c>mcp-grafana</c> may be installed (Homebrew's directories).</param>
/// <param name="OwnerAccount">The account name the hint for the owner's Keychain item shows.</param>
internal sealed record McpSetupSettings(
	string Home,
	string DataDirectory,
	string ToolVersion,
	string? SearchPath,
	IReadOnlyList<string> ExtraProgramDirectories,
	string OwnerAccount)
{
	/// <summary>Homebrew's bin directories on Apple silicon and on Intel Macs.</summary>
	public static IReadOnlyList<string> HomebrewDirectories { get; } = ["/opt/homebrew/bin", "/usr/local/bin"];
}
