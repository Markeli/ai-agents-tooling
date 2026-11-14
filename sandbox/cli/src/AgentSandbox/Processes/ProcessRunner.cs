using System.ComponentModel;
using System.Diagnostics;

namespace AgentSandbox.Processes;

/// <inheritdoc />
/// <param name="searchPath">The directories to look programs up in, as in PATH; null reads PATH at each start.</param>
internal sealed class ProcessRunner(string? searchPath = null) : IProcessRunner
{
	private const int CannotExecute = 126;
	private const int CommandNotFound = 127;

	private const UnixFileMode Executable = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

	public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
	{
		using var process = TryStart(spec, captureStdOut: spec.StdOut == OutputMode.Capture,
			captureStdErr: spec.StdErr == OutputMode.Capture, out var failure);
		if (process is null)
		{
			if (spec.StdErr == OutputMode.Inherit)
			{
				await Console.Error.WriteLineAsync(failure.StdErr);
				return failure with { StdErr = string.Empty };
			}

			return failure;
		}

		var stdOut = process.StartInfo.RedirectStandardOutput
			? process.StandardOutput.ReadToEndAsync(cancellationToken)
			: Task.FromResult(string.Empty);
		var stdErr = process.StartInfo.RedirectStandardError
			? process.StandardError.ReadToEndAsync(cancellationToken)
			: Task.FromResult(string.Empty);
		await process.WaitForExitAsync(cancellationToken);
		return new ProcessResult(process.ExitCode, await stdOut, await stdErr);
	}

	public async Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken)
	{
		// The terminal sends Ctrl+C to the whole foreground process group: let the child handle it and report its exit code
		static void KeepRunning(object? sender, ConsoleCancelEventArgs e) => e.Cancel = true;
		Console.CancelKeyPress += KeepRunning;
		try
		{
			using var process = TryStart(spec, captureStdOut: false, captureStdErr: false, out var failure);
			if (process is null)
			{
				await Console.Error.WriteLineAsync(failure.StdErr);
				return failure.ExitCode;
			}

			await process.WaitForExitAsync(cancellationToken);
			return process.ExitCode;
		}
		finally
		{
			Console.CancelKeyPress -= KeepRunning;
		}
	}

	/// <summary>
	/// Finds a program like a shell does: a name with a slash is a path, any other name is the first executable file of
	/// that name in <paramref name="path"/>. Unlike <see cref="Process.Start(ProcessStartInfo)"/>, never the current
	/// directory or the tool's own: a file named sbx or git in the repository would otherwise run instead.
	/// </summary>
	/// <returns>The program to start, or null when <paramref name="path"/> has none.</returns>
	internal static string? Resolve(string fileName, string? path)
	{
		if (fileName.Contains('/') || OperatingSystem.IsWindows())
		{
			return fileName;
		}

		// An empty entry means the current directory to a POSIX shell: skipped on purpose
		foreach (var directory in (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = Path.Combine(directory, fileName);
			if (File.Exists(candidate) && (File.GetUnixFileMode(candidate) & Executable) != 0)
			{
				return candidate;
			}
		}

		return null;
	}

	private Process? TryStart(ProcessSpec spec, bool captureStdOut, bool captureStdErr, out ProcessResult failure)
	{
		failure = new ProcessResult(0);
		var fileName = Resolve(spec.FileName, searchPath ?? Environment.GetEnvironmentVariable("PATH"));
		if (fileName is null)
		{
			failure = new ProcessResult(CommandNotFound, StdErr: $"{spec.FileName}: command not found");
			return null;
		}

		var startInfo = new ProcessStartInfo(fileName)
		{
			UseShellExecute = false,
			RedirectStandardOutput = captureStdOut,
			RedirectStandardError = captureStdErr,
		};
		foreach (var argument in spec.Arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (var (name, value) in spec.Environment)
		{
			startInfo.Environment[name] = value;
		}

		try
		{
			return Process.Start(startInfo);
		}
		catch (Win32Exception e)
		{
			// The exception's own message wraps the reason in the path and working directory; the error code alone reads
			// like a shell's "Permission denied" or "Exec format error"
			failure = new ProcessResult(CannotExecute, StdErr: $"{fileName}: {new Win32Exception(e.NativeErrorCode).Message}");
			return null;
		}
	}
}
