using System.CommandLine;

namespace AgentSandbox;

/// <summary>Runs <see cref="LauncherCommand"/> for a command line, with Claude Code's arguments after <c>--</c>.</summary>
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

		var command = new LauncherCommand(run, agentArguments, output).Root;

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
