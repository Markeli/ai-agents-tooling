using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgentSandbox.Platform;
using AgentSandbox.Processes;

namespace AgentSandbox.Mcp;

/// <summary>
/// <c>agent-sandbox mcp setup grafana</c>: installs keychain-env and grafana-mcp on the Mac and registers the server in
/// sbx (for the sandboxes, with the bot's token) and in Claude Code (with the owner's), see
/// docs/specs/2026-11-28-grafana-mcp.md. Idempotent: every step says what it did, found already done, or what is left to
/// the owner. Tokens are never read, only whether their Keychain items exist.
/// </summary>
internal sealed partial class GrafanaMcpSetup(IProcessRunner runner, ISecretStore secrets, TextWriter output)
{
	private const string BotAccount = "markeli-agent";
	private const string Prefix = "agent-sandbox: ";
	private const string McpGrafana = "mcp-grafana";

	/// <summary>Runs every step and returns 0, or throws a <see cref="LauncherException"/> naming the missing ones.</summary>
	public async Task<int> RunAsync(GrafanaSetupOptions options, McpSetupSettings settings, CancellationToken cancellationToken)
	{
		Validate(options);
		var missing = new List<string>();
		var bin = Path.GetFullPath(Path.Combine(settings.Home, ".local", "bin"));
		var command = Path.Combine(bin, EmbeddedMcpScripts.GrafanaMcp);

		await InstallScriptsAsync(settings, bin, missing, cancellationToken);
		await CheckMcpGrafanaAsync(settings, missing);
		await CheckItemAsync(options.AgentItem, BotAccount, "the bot's token, for the sandboxes", missing, cancellationToken);
		if (options.OwnerItem is { } ownerItem)
		{
			await CheckItemAsync(
				ownerItem, settings.OwnerAccount, "the owner's token, for Claude Code and Desktop", missing, cancellationToken);
		}

		await RegisterInSbxAsync(options, command, cancellationToken);
		if (options.OwnerItem is { } item)
		{
			await RegisterInClaudeCodeAsync(options, item, command, settings, missing, cancellationToken);
		}
		else
		{
			await SayAsync("claude code: skipped (no --owner-item)");
		}

		await PrintManualStepsAsync(options, command);

		if (missing.Count > 0)
		{
			throw new LauncherException($"{options.Name} is not set up yet: {string.Join("; ", missing)}");
		}

		await SayAsync($"{options.Name} is set up");
		return 0;
	}

	/// <summary>
	/// Whether <c>sbx mcp inspect --json</c> shows a registration of <paramref name="command"/> with
	/// <paramref name="arguments"/>.
	/// </summary>
	/// <remarks>
	/// The sbx docs don't describe the JSON, so this looks for any object with a <c>command</c> property (any case) equal
	/// to the command and an <c>args</c> property that is the arguments as an array or as sbx's comma-separated string;
	/// a <c>command</c> array holding the command and then the arguments counts too. Anything else, invalid JSON
	/// included, is no match: the caller then registers the server again, which costs a re-registration, never a wrong
	/// "already registered".
	/// </remarks>
	internal static bool IsSbxRegistration(string json, string command, IReadOnlyList<string> arguments)
	{
		try
		{
			return JsonNode.Parse(json) is { } root && Matches(root);
		}
		catch (JsonException)
		{
			return false;
		}

		bool Matches(JsonNode node) => node switch
		{
			JsonObject obj => IsRegistration(obj) || obj.Any(p => p.Value is not null && Matches(p.Value)),
			JsonArray array => array.Any(n => n is not null && Matches(n)),
			_ => false,
		};

		bool IsRegistration(JsonObject obj)
		{
			var commandValue = Property(obj, "command");
			if (commandValue is JsonArray commandLine)
			{
				return Strings(commandLine) is { } all && all.SequenceEqual([command, .. arguments]);
			}

			if (!IsString(commandValue, command))
			{
				return false;
			}

			return Property(obj, "args") switch
			{
				JsonArray args => Strings(args) is { } values && values.SequenceEqual(arguments),
				var args => IsString(args, string.Join(',', arguments)),
			};
		}

		static JsonNode? Property(JsonObject obj, string name) =>
			obj.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

		static bool IsString(JsonNode? node, string expected) =>
			node is JsonValue value && value.TryGetValue<string>(out var text) && text == expected;

		static string[]? Strings(JsonArray array)
		{
			var values = new string[array.Count];
			for (var i = 0; i < array.Count; i++)
			{
				if (array[i] is not JsonValue value || !value.TryGetValue<string>(out var text))
				{
					return null;
				}

				values[i] = text;
			}

			return values;
		}
	}

	/// <summary>The <c>Key: value</c> lines of <c>claude mcp get</c> (Scope, Status, Type, Command, Args, ...).</summary>
	internal static IReadOnlyDictionary<string, string> ParseClaudeServer(string text)
	{
		var fields = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var line in text.Split('\n').Select(l => l.Trim()))
		{
			var colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon > 0 && !line[..colon].Contains(' '))
			{
				fields.TryAdd(line[..colon], line[(colon + 1)..].Trim());
			}
		}

		return fields;
	}

	private static void Validate(GrafanaSetupOptions options)
	{
		// The URL and the item go into sbx's comma-separated --args, and into argument lists printed for a shell
		if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url)
			|| url.Scheme is not ("https" or "http")
			|| string.IsNullOrEmpty(url.Host)
			|| options.Url.Any(c => c == ',' || char.IsWhiteSpace(c) || char.IsControl(c)))
		{
			throw new LauncherException($"--url must be an http(s) URL without commas or spaces, got '{options.Url}'");
		}

		if (!ValidName().IsMatch(options.Name))
		{
			throw new LauncherException(
				$"invalid --name '{options.Name}': use letters, digits, '-' and '_', starting with a letter or digit");
		}

		foreach (var (option, item) in new[] { ("--agent-item", options.AgentItem), ("--owner-item", options.OwnerItem) })
		{
			if (item is not null && !ValidItem().IsMatch(item))
			{
				throw new LauncherException(
					$"invalid {option} '{item}': use letters, digits and . _ / @ : + -, starting with a letter or digit");
			}
		}
	}

	private async Task InstallScriptsAsync(McpSetupSettings settings, string bin, List<string> missing, CancellationToken ct)
	{
		var (directory, changed) = await EmbeddedMcpScripts.ExtractAsync(settings.DataDirectory, settings.ToolVersion, ct);
		await SayAsync(changed ? $"scripts: wrote {directory}" : $"scripts: {directory} up to date");

		Directory.CreateDirectory(bin);
		foreach (var name in new[] { EmbeddedMcpScripts.KeychainEnv, EmbeddedMcpScripts.GrafanaMcp })
		{
			var link = Path.Combine(bin, name);
			var target = Path.GetFullPath(Path.Combine(directory, name));
			switch (Link(link, target))
			{
				case LinkState.Linked:
					await SayAsync($"scripts: linked {link} -> {target}");
					break;
				case LinkState.AlreadyLinked:
					await SayAsync($"scripts: {link} already links to {target}");
					break;
				default:
					await SayAsync(
						$"scripts: {link} exists and is not a link into agent-sandbox/mcp/; left alone: "
						+ $"move it away and run this again");
					missing.Add($"{link} is in the way");
					break;
			}
		}

		var path = (settings.SearchPath ?? string.Empty).Split(Path.PathSeparator);
		if (!path.Contains(bin) && !path.Contains(bin + Path.DirectorySeparatorChar))
		{
			await SayAsync($"note: {bin} is not in PATH; the registrations use full paths, so that only matters in a shell");
		}
	}

	// Replaces only a link this tool made (one into an agent-sandbox/mcp/ directory, e.g. of an older version); the
	// new link is renamed over the old one, so the path never disappears for a server starting at the same moment
	private static LinkState Link(string link, string target)
	{
		var info = new FileInfo(link);
		if (info.LinkTarget is { } current)
		{
			var resolved = Path.GetFullPath(current, Path.GetDirectoryName(link)!);
			if (resolved == target)
			{
				return LinkState.AlreadyLinked;
			}

			var ours = $"{Path.DirectorySeparatorChar}agent-sandbox{Path.DirectorySeparatorChar}mcp{Path.DirectorySeparatorChar}";
			if (!resolved.Contains(ours, StringComparison.Ordinal))
			{
				return LinkState.Foreign;
			}
		}
		else if (info.Exists || Directory.Exists(link))
		{
			return LinkState.Foreign;
		}

		var temporary = Path.Combine(Path.GetDirectoryName(link)!, $".{Path.GetFileName(link)}.{Guid.NewGuid():N}.tmp");
		File.CreateSymbolicLink(temporary, target);
		try
		{
			File.Move(temporary, link, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}

		return LinkState.Linked;
	}

	private async Task CheckMcpGrafanaAsync(McpSetupSettings settings, List<string> missing)
	{
		var path = string.Join(
			Path.PathSeparator, new[] { settings.SearchPath ?? string.Empty }.Concat(settings.ExtraProgramDirectories));
		if (ProcessRunner.Resolve(McpGrafana, path) is { } found && found != McpGrafana)
		{
			await SayAsync($"mcp-grafana: found {found}");
			return;
		}

		await SayAsync("mcp-grafana: not found in PATH or Homebrew's directories; install it: brew install mcp-grafana");
		missing.Add("mcp-grafana is not installed");
	}

	private async Task CheckItemAsync(string item, string account, string purpose, List<string> missing, CancellationToken ct)
	{
		if (await secrets.ContainsAsync(item, ct))
		{
			await SayAsync($"keychain: {item} found");
			return;
		}

		await SayAsync($"keychain: {item} ({purpose}) not found; create it:");
		await output.WriteLineAsync(
			"    1. In Grafana: Administration → Users and access → Service accounts → Add service account,\n"
			+ "       one per client (the bot and the owner), role Editor → Add service account token (expiry: one year).\n"
			+ $"    2. security add-generic-password -U -s {item} -a {account} -w\n"
			+ "       It prompts for the token, so the token stays out of the shell history.\n"
			+ "    3. On first use macOS asks whether security may access the item: choose \"Always Allow\".");
		missing.Add($"keychain item {item} is missing");
	}

	// sbx's docs don't show the output of mcp ls or inspect. "mcp ls --quiet" prints only names (one per line), so
	// presence is taken from it; the arguments of an existing registration from "mcp inspect --json", parsed
	// defensively (IsSbxRegistration): when it can't confirm the same command and arguments, the server is removed
	// and added again, which is safe because the registration holds no secret.
	private async Task RegisterInSbxAsync(GrafanaSetupOptions options, string command, CancellationToken ct)
	{
		string[] arguments = [options.Url, options.AgentItem];
		var list = await RunOrThrowAsync(Sbx("mcp", "ls", "--quiet"), "cannot list the sbx MCP servers", ct);
		var names = list.StdOut.Split('\n').Select(l => l.Trim());
		if (names.Contains(options.Name))
		{
			var inspect = await runner.RunAsync(Sbx("mcp", "inspect", options.Name, "--json"), ct);
			if (inspect.Succeeded && IsSbxRegistration(inspect.StdOut, command, arguments))
			{
				await SayAsync($"sbx: {options.Name} already registered");
				return;
			}

			await RunOrThrowAsync(Sbx("mcp", "rm", "--force", options.Name), $"cannot remove sbx MCP server {options.Name}", ct);
			await AddToSbxAsync(options.Name, command, arguments, ct);
			await SayAsync($"sbx: re-registered {options.Name} (its command or arguments differed)");
			return;
		}

		await AddToSbxAsync(options.Name, command, arguments, ct);
		await SayAsync($"sbx: registered {options.Name}");
	}

	private Task AddToSbxAsync(string name, string command, string[] arguments, CancellationToken ct) =>
		RunOrThrowAsync(
			Sbx("mcp", "add", name, "--command", command, "--args", string.Join(',', arguments)),
			$"cannot register sbx MCP server {name}",
			ct);

	// claude mcp get prints "Scope: User config (...)", "Command: <path>" and "Args: <args joined by spaces>" (checked
	// with Claude Code 2.1); it also tries to start the server, so the first run may show the Keychain prompt
	private async Task RegisterInClaudeCodeAsync(
		GrafanaSetupOptions options,
		string ownerItem,
		string command,
		McpSetupSettings settings,
		List<string> missing,
		CancellationToken ct)
	{
		var add = Claude("mcp", "add", "--scope", "user", options.Name, "--", command, options.Url, ownerItem);
		if (ProcessRunner.Resolve("claude", settings.SearchPath) is null)
		{
			await SayAsync($"claude code: claude is not in PATH, skipped; to register it later: {add.CommandLine}");
			return;
		}

		var get = await runner.RunAsync(Claude("mcp", "get", options.Name), ct);
		if (!get.Succeeded)
		{
			await RunOrThrowAsync(add, $"cannot register {options.Name} in Claude Code", ct);
			await SayAsync($"claude code: registered {options.Name} (user scope)");
			return;
		}

		var server = ParseClaudeServer(get.StdOut);
		var scope = server.GetValueOrDefault("Scope", string.Empty);
		if (!scope.StartsWith("User", StringComparison.Ordinal))
		{
			await SayAsync(
				$"claude code: {options.Name} is configured in another scope ({scope}), which hides a user-scope server; "
				+ $"remove it there (claude mcp remove {options.Name} --scope local|project) and run this again");
			missing.Add($"{options.Name} in another Claude Code scope");
			return;
		}

		if (server.GetValueOrDefault("Command") == command
			&& server.GetValueOrDefault("Args") == $"{options.Url} {ownerItem}")
		{
			await SayAsync($"claude code: {options.Name} already registered (user scope)");
			return;
		}

		await RunOrThrowAsync(
			Claude("mcp", "remove", "--scope", "user", options.Name), $"cannot remove {options.Name} from Claude Code", ct);
		await RunOrThrowAsync(add, $"cannot register {options.Name} in Claude Code", ct);
		await SayAsync($"claude code: re-registered {options.Name} (its command or arguments differed)");
	}

	private async Task PrintManualStepsAsync(GrafanaSetupOptions options, string command)
	{
		var entry = new JsonObject
		{
			[options.Name] = new JsonObject
			{
				["command"] = command,
				["args"] = new JsonArray(options.Url, options.OwnerItem ?? "<owner keychain item>"),
			},
		};
		var json = entry.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true,

			// Printed for a terminal, not a web page: keep <owner keychain item> readable
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		});

		await output.WriteLineAsync();
		await output.WriteLineAsync("Left to do by hand (once):");
		await output.WriteLineAsync(
			"- Claude Desktop: merge this into \"mcpServers\" of "
			+ "~/Library/Application Support/Claude/claude_desktop_config.json, then restart the app:");
		await output.WriteLineAsync(Indent(json, "    "));
		await output.WriteLineAsync(
			"- Grafana server settings that keep data inside Grafana (no external snapshots, no public dashboards):\n"
			+ "    GF_SNAPSHOTS_EXTERNAL_ENABLED=false\n"
			+ "    GF_PUBLIC_DASHBOARDS_ENABLED=false");
		await output.WriteLineAsync(
			$"- Sandboxes attach {options.Name} themselves (mcp-find, then mcp-add); nothing to do per sandbox.");
		await output.WriteLineAsync(
			"- Check: ask an agent to list the Grafana data sources, to show the error log lines of the last hour, and to\n"
			+ "  publish a snapshot externally, which Grafana must refuse (403 \"External dashboard creation is disabled\").\n"
			+ $"  sbx mcp ls and claude mcp get {options.Name} show the registrations.");
		await output.WriteLineAsync();
	}

	private async Task<ProcessResult> RunOrThrowAsync(ProcessSpec spec, string failure, CancellationToken ct)
	{
		var result = await runner.RunAsync(spec, ct);
		return result.Succeeded
			? result
			: throw new LauncherException($"{failure} (exit {result.ExitCode}): {result.StdOut}{result.StdErr}".TrimEnd());
	}

	private Task SayAsync(string message) => output.WriteLineAsync(Prefix + message);

	private static string Indent(string text, string indent) =>
		string.Join('\n', text.Split('\n').Select(line => indent + line));

	private static ProcessSpec Sbx(params string[] arguments) => new("sbx", arguments);

	private static ProcessSpec Claude(params string[] arguments) => new("claude", arguments);

	[GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$")]
	private static partial Regex ValidName();

	[GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/@:+-]*$")]
	private static partial Regex ValidItem();

	private enum LinkState
	{
		Linked,
		AlreadyLinked,
		Foreign,
	}
}
