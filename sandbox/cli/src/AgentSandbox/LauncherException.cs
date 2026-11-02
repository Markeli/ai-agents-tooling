namespace AgentSandbox;

/// <summary>Stops the launcher with an exit code and, unless the failing program already said why, a message.</summary>
internal sealed class LauncherException(string? message, int exitCode = 1) : Exception(message ?? string.Empty)
{
	public int ExitCode { get; } = exitCode;

	public bool HasMessage { get; } = message is not null;

	/// <summary>Exits with a failed program's exit code; its stderr was already shown, as with <c>set -e</c>.</summary>
	public static LauncherException ExitWith(int exitCode) => new(null, exitCode);
}
