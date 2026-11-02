using System.CommandLine;

namespace AgentSandbox;

/// <summary>The command line: <c>agent-sandbox [--no-attach] [--recreate] [--yes] [--name NAME] [REPO_PATH] [-- ARGS...]</c>.</summary>
internal static class Cli
{
	private const string Separator = "--";

	/// <summary>Parses <paramref name="args"/>, runs <paramref name="run"/> and returns the exit code.</summary>
	/// <remarks>
	/// Everything after the first <c>--</c> goes to Claude Code untouched, so it is split off before parsing. A
	/// <see cref="LauncherException"/> becomes <c>agent-sandbox: &lt;message&gt;</c> on <paramref name="error"/>.
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

		var noAttach = new Option<bool>("--no-attach") { Description = "Create the sandbox if needed, but don't attach to it" };
		var recreate = new Option<bool>("--recreate")
		{
			Description = "Remove and recreate the sandbox (refused while it has commits not pushed to origin)",
		};
		var yes = new Option<bool>("--yes") { Description = "With --recreate, skip the confirmation of sbx rm" };
		var name = new Option<string>("--name")
		{
			Description = "Sandbox name (lowercase letters, digits, hyphens); defaults to the repository directory name",
		};
		var repo = new Argument<string>("REPO_PATH")
		{
			Description = "The repository, or a directory inside it",
			Arity = ArgumentArity.ZeroOrOne,
			DefaultValueFactory = _ => ".",
		};
		// System.CommandLine takes an option-like token as a value: --name --recreate would name the sandbox
		// "--recreate", and a mistyped --flag would become the repository path
		name.Validators.Add(result =>
		{
			if (result.Tokens.FirstOrDefault()?.Value is { } value && value.StartsWith('-'))
			{
				result.AddError($"--name needs a value, got the option '{value}'");
			}
		});
		repo.Validators.Add(result =>
		{
			if (result.Tokens.FirstOrDefault()?.Value is { } value && value.StartsWith('-'))
			{
				result.AddError($"unknown option '{value}' (write ./{value} for a path)");
			}
		});
		var command = new RootCommand(
			"Creates (once) and attaches to a clone-mode sandbox for a repository, running Claude Code as markeli-agent. "
			+ "Arguments after -- go to Claude Code.")
		{
			noAttach, recreate, yes, name, repo,
		};
		command.SetAction(async (result, token) =>
		{
			var chosenName = result.GetValue(name);
			if (chosenName is { Length: 0 })
			{
				throw new LauncherException("--name must not be empty; omit --name to use the repo-derived name");
			}

			var options = new LauncherOptions
			{
				RepoPath = result.GetValue(repo) ?? ".",
				Name = chosenName,
				NoAttach = result.GetValue(noAttach),
				Recreate = result.GetValue(recreate),
				Yes = result.GetValue(yes),
				AgentArguments = agentArguments,
			};
			return await run(options, token);
		});

		// A repository path starting with @ is a path, not a response file
		var parsed = command.Parse(own, new ParserConfiguration { ResponseFileTokenReplacer = null });
		try
		{
			return await parsed.InvokeAsync(
				new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false },
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
	}
}
