using System.ComponentModel;
using System.Diagnostics;

namespace AgentSandbox.Processes;

/// <inheritdoc />
internal sealed class ProcessRunner : IProcessRunner
{
	private const int CommandNotFound = 127;

	public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
	{
		var startInfo = CreateStartInfo(spec);
		startInfo.RedirectStandardOutput = spec.StdOut == OutputMode.Capture;
		startInfo.RedirectStandardError = spec.StdErr == OutputMode.Capture;

		using var process = TryStart(startInfo, out var notFound);
		if (process is null)
		{
			if (spec.StdErr == OutputMode.Inherit)
			{
				await Console.Error.WriteLineAsync(notFound);
				return new ProcessResult(CommandNotFound);
			}

			return new ProcessResult(CommandNotFound, StdErr: notFound);
		}

		var stdOut = startInfo.RedirectStandardOutput
			? process.StandardOutput.ReadToEndAsync(cancellationToken)
			: Task.FromResult(string.Empty);
		var stdErr = startInfo.RedirectStandardError
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
			using var process = TryStart(CreateStartInfo(spec), out var notFound);
			if (process is null)
			{
				await Console.Error.WriteLineAsync(notFound);
				return CommandNotFound;
			}

			await process.WaitForExitAsync(cancellationToken);
			return process.ExitCode;
		}
		finally
		{
			Console.CancelKeyPress -= KeepRunning;
		}
	}

	private static ProcessStartInfo CreateStartInfo(ProcessSpec spec)
	{
		var startInfo = new ProcessStartInfo(spec.FileName) { UseShellExecute = false };
		foreach (var argument in spec.Arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (var (name, value) in spec.Environment)
		{
			startInfo.Environment[name] = value;
		}

		return startInfo;
	}

	private static Process? TryStart(ProcessStartInfo startInfo, out string notFound)
	{
		notFound = $"{startInfo.FileName}: command not found";
		try
		{
			return Process.Start(startInfo);
		}
		catch (Win32Exception)
		{
			return null;
		}
	}
}
