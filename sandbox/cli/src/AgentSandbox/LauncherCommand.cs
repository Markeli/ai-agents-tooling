using System.CommandLine;

namespace AgentSandbox;

/// <summary>
/// The command line: <c>agent-sandbox [--no-attach] [--recreate] [--yes] [--name NAME] [REPO_PATH] [-- AGENT_ARGS]</c>,
/// plus <c>agent-sandbox help</c>; turns it into <see cref="LauncherOptions"/> and runs the launcher.
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

	private readonly Option<string> _name = new("--name")
	{
		Description = "Sandbox name (lowercase letters, digits, hyphens); defaults to the repository directory name",
	};

	private readonly Argument<string> _repo = new("REPO_PATH")
	{
		Description = "The repository, or a directory inside it (write ./help for a directory named help)",
		Arity = ArgumentArity.ZeroOrOne,
		DefaultValueFactory = _ => ".",
	};

	private readonly RootCommand _root;

	private LauncherCommand(
		Func<LauncherOptions, CancellationToken, Task<int>> run,
		IReadOnlyList<string> agentArguments,
		TextWriter output)
	{
		RejectOptionsAsValues();
		_root = new RootCommand(
			"Creates (once) and attaches to a clone-mode sandbox for a repository, running the agent as markeli-agent. "
			+ "Arguments after -- go to the agent.")
		{
			_noAttach, _recreate, _yes, _name, _repo, CreateHelpCommand(output),
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
		TextWriter output,
		TextWriter error,
		CancellationToken cancellationToken)
	{
		var separator = Array.IndexOf(args, Separator);
		var own = separator < 0 ? args : args[..separator];
		var agentArguments = separator < 0 ? [] : args[(separator + 1)..];

		var command = new LauncherCommand(run, agentArguments, output);

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

	// The same text as --help, for people who type the command first
	private Command CreateHelpCommand(TextWriter output)
	{
		var help = new Command("help", "Show help and usage information");
		help.SetAction(_ => _root.Parse(["--help"]).Invoke(new InvocationConfiguration { Output = output }));
		return help;
	}
}
