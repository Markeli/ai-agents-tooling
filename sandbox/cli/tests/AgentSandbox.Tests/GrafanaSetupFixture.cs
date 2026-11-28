using System.Runtime.Versioning;
using System.Text.Json;
using AgentSandbox.Mcp;
using AgentSandbox.Processes;

namespace AgentSandbox.Tests;

/// <summary>
/// A Mac in a temporary directory (home, data directory, a PATH directory with mcp-grafana and claude), the bot's
/// Keychain item, and sbx and Claude Code simulated by their MCP registrations.
/// </summary>
// Unix file modes and symlinks; the setup runs on macOS, CI on Linux
[UnsupportedOSPlatform("windows")]
internal sealed class GrafanaSetupFixture : IDisposable
{
	public const string Url = "https://grafana.example.com";
	public const string Version = "1.2.3";

	private readonly string _root = Directory.CreateTempSubdirectory("agent-sandbox-mcp-tests-").FullName;

	public GrafanaSetupFixture()
	{
		Directory.CreateDirectory(Home);
		Directory.CreateDirectory(PathDirectory);
		AddProgram("mcp-grafana");
		AddProgram("claude");
		Runner.OnProgram("sbx", Sbx).OnProgram("claude", Claude);
	}

	public string Home => Path.Combine(_root, "home");

	public string Data => Path.Combine(_root, "data");

	public string PathDirectory => Path.Combine(_root, "bin");

	public string Bin => Path.Combine(Home, ".local", "bin");

	public string Scripts => Path.Combine(Data, "agent-sandbox", "mcp", Version);

	public string Command => Path.Combine(Bin, "grafana-mcp");

	public FakeProcessRunner Runner { get; } = new();

	public FakeSecretStore Secrets { get; } = new("agent/personal-grafana", "owner/personal-grafana");

	public StringWriter Output { get; } = new();

	/// <summary>sbx's registrations: name → (command, arguments).</summary>
	public Dictionary<string, (string Command, string[] Arguments)> SbxServers { get; } = [];

	/// <summary>Claude Code's registrations: name → (scope, command, arguments).</summary>
	public Dictionary<string, (string Scope, string Command, string[] Arguments)> ClaudeServers { get; } = [];

	/// <summary>What <c>sbx mcp inspect --json</c> prints for a registration; the docs don't show it, so tests vary it.</summary>
	public Func<string, string, string[], string> SbxInspectJson { get; set; } = (name, command, arguments) =>
		JsonSerializer.Serialize(new { name, type = "stdio", command, args = arguments });

	public void Dispose() => Directory.Delete(_root, recursive: true);

	public void AddProgram(string name)
	{
		var path = Path.Combine(PathDirectory, name);
		File.WriteAllText(path, "#!/bin/sh\n");
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}

	public void RemoveProgram(string name) => File.Delete(Path.Combine(PathDirectory, name));

	public McpSetupSettings Settings => new(Home, Data, Version, PathDirectory, [], "owner");

	public Task<int> RunAsync(GrafanaSetupOptions options) =>
		new GrafanaMcpSetup(Runner, Secrets, Output).RunAsync(options, Settings, CancellationToken.None);

	/// <summary>Runs again with a fresh record of calls and output.</summary>
	public Task<int> RunAgainAsync(GrafanaSetupOptions options)
	{
		Runner.Calls.Clear();
		Output.GetStringBuilder().Clear();
		return RunAsync(options);
	}

	private ProcessResult? Sbx(ProcessSpec spec) => spec.Arguments switch
	{
		["mcp", "ls", "--quiet"] => new ProcessResult(0, string.Concat(SbxServers.Keys.Select(k => k + "\n"))),
		["mcp", "inspect", var name, "--json"] => SbxServers.TryGetValue(name, out var server)
			? new ProcessResult(0, SbxInspectJson(name, server.Command, server.Arguments))
			: new ProcessResult(1, StdErr: $"no MCP server {name}"),
		["mcp", "add", var name, "--command", var command, "--args", var arguments] => Add(name, command, arguments),
		["mcp", "rm", "--force", var name] => SbxServers.Remove(name) ? new ProcessResult(0) : new ProcessResult(1),
		_ => null,
	};

	private ProcessResult Add(string name, string command, string arguments)
	{
		if (SbxServers.ContainsKey(name))
		{
			return new ProcessResult(1, StdErr: $"MCP server {name} already exists");
		}

		SbxServers[name] = (command, arguments.Split(','));
		return new ProcessResult(0);
	}

	// The format of Claude Code 2.1's claude mcp get
	private ProcessResult? Claude(ProcessSpec spec)
	{
		switch (spec.Arguments)
		{
			case ["mcp", "get", var name]:
				if (!ClaudeServers.TryGetValue(name, out var server))
				{
					return new ProcessResult(1, StdErr: $"No MCP server named \"{name}\".");
				}

				return new ProcessResult(
					0,
					$"{name}:\n  Scope: {server.Scope}\n  Status: ✔ Connected\n  Type: stdio\n  Command: {server.Command}\n"
					+ $"  Args: {string.Join(' ', server.Arguments)}\n  Environment:\n\n"
					+ $"To remove this server, run: claude mcp remove {name} -s user\n");
			case ["mcp", "remove", "--scope", "user", var name]:
				return ClaudeServers.TryGetValue(name, out var existing) && existing.Scope.StartsWith("User")
					&& ClaudeServers.Remove(name)
					? new ProcessResult(0)
					: new ProcessResult(1, StdErr: $"No user-scoped MCP server found with name: {name}");
			case ["mcp", "add", "--scope", "user", var name, "--", var command, ..]:
				if (ClaudeServers.ContainsKey(name))
				{
					return new ProcessResult(1, StdErr: $"MCP server {name} already exists in user config");
				}

				ClaudeServers[name] = ("User config (available in all your projects)", command, [.. spec.Arguments.Skip(7)]);
				return new ProcessResult(0);
			default:
				return null;
		}
	}
}
