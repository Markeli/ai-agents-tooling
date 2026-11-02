using System.CommandLine;

namespace AgentSandbox;

/// <summary>
/// The root command: <c>agent-sandbox [--no-attach] [--recreate] [--yes] [--name NAME] [REPO_PATH]</c>, plus
/// <c>agent-sandbox help</c>; turns a parse result into <see cref="LauncherOptions"/>.
/// </summary>
internal sealed class LauncherCommand
{
	private readonly Option<bool> _noAttach = new("--no-attach")
	{
		Description = "Create the sandbox if needed, but don't attach to it",
	};

	private readonly Option<bool> _recreate = new("--recreate")
	{
		Description = "Remove and recreate the sandbox (refused while it has commits not pushed to origin)",
	};

	private readonly Option<bool> _yes = new("--yes") { Description = "With --recreate, skip the confirmation of sbx rm" };

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

	/// <param name="run">Runs the launcher with the parsed options.</param>
	/// <param name="agentArguments">The arguments after <c>--</c>, split off before parsing.</param>
	/// <param name="output">Where <c>help</c> writes.</param>
	public LauncherCommand(
		Func<LauncherOptions, CancellationToken, Task<int>> run,
		IReadOnlyList<string> agentArguments,
		TextWriter output)
	{
		RejectOptionsAsValues();
		Root = new RootCommand(
			"Creates (once) and attaches to a clone-mode sandbox for a repository, running Claude Code as markeli-agent. "
			+ "Arguments after -- go to Claude Code.")
		{
			_noAttach, _recreate, _yes, _name, _repo, CreateHelpCommand(output),
		};
		Root.SetAction((result, cancellationToken) => run(ToOptions(result, agentArguments), cancellationToken));
	}

	public RootCommand Root { get; }

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
		help.SetAction(_ => Root.Parse(["--help"]).Invoke(new InvocationConfiguration { Output = output }));
		return help;
	}
}
