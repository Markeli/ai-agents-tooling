namespace AgentSandbox.Processes;

/// <summary>Where a child process writes one of its output streams.</summary>
internal enum OutputMode
{
	/// <summary>Read into <see cref="ProcessResult"/>; nothing reaches the terminal.</summary>
	Capture,

	/// <summary>Written straight to the launcher's own stream.</summary>
	Inherit,
}

/// <summary>A program to run, with its arguments, extra environment variables and output handling.</summary>
internal sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments)
{
	public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

	public OutputMode StdOut { get; init; } = OutputMode.Capture;

	public OutputMode StdErr { get; init; } = OutputMode.Capture;

	/// <summary>The program and its arguments separated by spaces, for messages and tests; not shell-quoted.</summary>
	public string CommandLine => Arguments.Count == 0 ? FileName : $"{FileName} {string.Join(' ', Arguments)}";

	public override string ToString() => CommandLine;
}

/// <summary>The exit code and the captured output of a finished process; streams that were inherited are empty.</summary>
internal sealed record ProcessResult(int ExitCode, string StdOut = "", string StdErr = "")
{
	public bool Succeeded => ExitCode == 0;
}
