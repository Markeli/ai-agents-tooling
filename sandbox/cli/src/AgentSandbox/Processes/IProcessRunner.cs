namespace AgentSandbox.Processes;

/// <summary>Runs external programs (sbx, git, ssh-agent, security).</summary>
internal interface IProcessRunner
{
	/// <summary>Runs a program to completion; stdin is inherited, stdout and stderr as <paramref name="spec"/> says.</summary>
	/// <remarks>A program that can't be started yields exit code 127 and a "command not found" message on stderr.</remarks>
	Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);

	/// <summary>
	/// Runs a program attached to the terminal, as a shell's <c>exec</c> would: all streams inherited, and Ctrl+C goes to
	/// the program instead of stopping the launcher.
	/// </summary>
	Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken);
}
