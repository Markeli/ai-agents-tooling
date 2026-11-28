using System.CommandLine;
using AgentSandbox.Mcp;

namespace AgentSandbox;

/// <summary>
/// The command line: <c>agent-sandbox [--no-attach] [--recreate] [--yes] [--force-pull] [--name NAME] [REPO_PATH] [-- AGENT_ARGS]</c>,
/// plus <c>agent-sandbox help</c> and <c>agent-sandbox mcp setup grafana --url URL [...]</c>; turns it into
/// <see cref="LauncherOptions"/> and runs the launcher, or into <see cref="GrafanaSetupOptions"/> and runs the setup.
/// </summary>
internal sealed class LauncherCommand
{
	private const string Separator = "--";

	// Arity zero: a flag never takes the next token as its value, so in "--yes false" the "false" is REPO_PATH
	private readonly Option<bool> _noAttach = new("--no-attach")
	{
		Description = "Create the sandbox if needed, but don't attach to it",
		Arity = ArgumentArity.Zero,
	};

	private readonly Option<bool> _recreate = new("--recreate")
	{
		Description = "Remove and recreate the sandbox (refused while it has commits not pushed to origin)",
		Arity = ArgumentArity.Zero,
	};

	private readonly Option<bool> _yes = new("--yes")
	{
		Description = "With --recreate, skip the confirmation of sbx rm",
		Arity = ArgumentArity.Zero,
	};

	private readonly Option<bool> _forcePull = new("--force-pull")
	{
		Description = "When the sandbox is created (first run or --recreate), pull its image even if there is a local copy",
		Arity = ArgumentArity.Zero,
	};

	private readonly Option<string> _name = new("--name")
	{
		Description = "Sandbox name (lowercase letters, digits, hyphens); defaults to the repository directory name",
	};

	private readonly Argument<string> _repo = new("REPO_PATH")
	{
		Description = "The repository, or a directory inside it (write ./help or ./mcp for a directory named help or mcp)",
		Arity = ArgumentArity.ZeroOrOne,
		DefaultValueFactory = _ => ".",
	};

	private readonly RootCommand _root;

	private LauncherCommand(
		Func<LauncherOptions, CancellationToken, Task<int>> run,
		Func<GrafanaSetupOptions, CancellationToken, Task<int>> setupGrafana,
		IReadOnlyList<string> agentArguments,
		TextWriter output)
	{
		RejectOptionsAsValues();
		_root = new RootCommand(
			"Creates (once) and attaches to a clone-mode sandbox for a repository, running the agent as markeli-agent. "
			+ "Arguments after -- go to the agent.")
		{
			_noAttach, _recreate, _yes, _forcePull, _name, _repo, CreateHelpCommand(output),
			CreateMcpCommand(setupGrafana, agentArguments),
		};
		_root.SetAction((result, cancellationToken) => run(ToOptions(result, agentArguments), cancellationToken));
	}

	/// <summary>Parses <paramref name="args"/>, runs <paramref name="run"/> and returns the exit code.</summary>
	/// <remarks>
	/// Everything after the first <c>--</c> goes to the agent untouched. It is split off before parsing because
	/// System.CommandLine would bind the first token after <c>--</c> to <c>REPO_PATH</c>. A
	/// <see cref="LauncherException"/> or a file system error becomes <c>agent-sandbox: &lt;message&gt;</c> on
	/// <paramref name="error"/>.
	/// </remarks>
	public static async Task<int> InvokeAsync(
		string[] args,
		Func<LauncherOptions, CancellationToken, Task<int>> run,
		Func<GrafanaSetupOptions, CancellationToken, Task<int>> setupGrafana,
		TextWriter output,
		TextWriter error,
		CancellationToken cancellationToken)
	{
		var separator = Array.IndexOf(args, Separator);
		var own = separator < 0 ? args : args[..separator];
		var agentArguments = separator < 0 ? [] : args[(separator + 1)..];

		var command = new LauncherCommand(run, setupGrafana, agentArguments, output);

		// A repository path starting with @ is a path, not a response file
		var parsed = command._root.Parse(own, new ParserConfiguration { ResponseFileTokenReplacer = null });
		try
		{
			// No termination timeout: System.CommandLine would otherwise cancel the action on Ctrl+C and SIGTERM, while
			// the launcher leaves signals to the programs it runs, as the bash launcher did
			return await parsed.InvokeAsync(
				new InvocationConfiguration
				{
					Output = output,
					Error = error,
					EnableDefaultExceptionHandler = false,
					ProcessTerminationTimeout = null,
				},
				cancellationToken);
		}
		catch (LauncherException e)
		{
			if (e.HasMessage)
			{
				await error.WriteLineAsync($"agent-sandbox: {e.Message}");
			}

			return e.ExitCode;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			// The file system said no (kit directory, ssh socket): a message, not a stack trace
			await error.WriteLineAsync($"agent-sandbox: {e.Message}");
			return 1;
		}
	}

	private LauncherOptions ToOptions(ParseResult result, IReadOnlyList<string> agentArguments)
	{
		var name = result.GetValue(_name);
		if (name is { Length: 0 })
		{
			throw new LauncherException("--name must not be empty; omit --name to use the repo-derived name");
		}

		return new LauncherOptions
		{
			RepoPath = result.GetValue(_repo) ?? ".",
			Name = name,
			NoAttach = result.GetValue(_noAttach),
			Recreate = result.GetValue(_recreate),
			Yes = result.GetValue(_yes),
			ForcePull = result.GetValue(_forcePull),
			AgentArguments = agentArguments,
		};
	}

	// System.CommandLine takes an option-like token as a value: --name --recreate would name the sandbox
	// "--recreate", and a mistyped --flag would become the repository path
	private void RejectOptionsAsValues()
	{
		_name.Validators.Add(result =>
		{
			if (result.Tokens.FirstOrDefault()?.Value is { } value && value.StartsWith('-'))
			{
				result.AddError($"--name needs a value, got the option '{value}'");
			}
		});
		_repo.Validators.Add(result =>
		{
			if (result.Tokens.FirstOrDefault()?.Value is { } value && value.StartsWith('-'))
			{
				result.AddError($"unknown option '{value}' (write ./{value} for a path)");
			}
		});
	}

	private static void RejectOptionAsValue(Option<string> option) =>
		option.Validators.Add(result =>
		{
			if (result.Tokens.FirstOrDefault()?.Value is { } value && value.StartsWith('-'))
			{
				result.AddError($"{option.Name} needs a value, got the option '{value}'");
			}
		});

	// agent-sandbox mcp setup grafana: a subcommand like help, so a repository directory named mcp is written ./mcp
	private static Command CreateMcpCommand(
		Func<GrafanaSetupOptions, CancellationToken, Task<int>> setupGrafana,
		IReadOnlyList<string> agentArguments)
	{
		var url = new Option<string>("--url")
		{
			Description = "The Grafana base URL, e.g. https://grafana.example.com",
			Required = true,
		};
		var name = new Option<string>("--name")
		{
			Description = "The server's name in sbx and Claude Code",
			DefaultValueFactory = _ => GrafanaSetupOptions.DefaultName,
		};
		var agentItem = new Option<string>("--agent-item")
		{
			Description = "Keychain item with the bot's service account token, for the sandboxes",
			DefaultValueFactory = _ => GrafanaSetupOptions.DefaultAgentItem,
		};
		var ownerItem = new Option<string>("--owner-item")
		{
			Description = "Keychain item with the owner's token; registers the server in Claude Code too",
		};
		foreach (var option in new[] { url, name, agentItem, ownerItem })
		{
			RejectOptionAsValue(option);
		}

		var grafana = new Command(
			"grafana",
			"Install grafana-mcp and keychain-env into ~/.local/bin and register the server in sbx (bot's token) and, "
			+ "with --owner-item, in Claude Code; prints what is left to do by hand")
		{
			url, name, agentItem, ownerItem,
		};
		grafana.SetAction((result, cancellationToken) =>
		{
			if (agentArguments.Count > 0)
			{
				throw new LauncherException("arguments after -- are only for the agent, not for mcp setup");
			}

			return setupGrafana(
				new GrafanaSetupOptions
				{
					Url = result.GetRequiredValue(url),
					Name = result.GetRequiredValue(name),
					AgentItem = result.GetRequiredValue(agentItem),
					OwnerItem = result.GetValue(ownerItem),
				},
				cancellationToken);
		});

		return new Command("mcp", "MCP servers on this Mac that sandboxes reach through the sbx MCP gateway")
		{
			new Command("setup", "Install and register an MCP server") { grafana },
		};
	}

	// The same text as --help, for people who type the command first
	private Command CreateHelpCommand(TextWriter output)
	{
		var help = new Command("help", "Show help and usage information");
		help.SetAction(_ => _root.Parse(["--help"]).Invoke(new InvocationConfiguration { Output = output }));
		return help;
	}
}
