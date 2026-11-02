using AgentSandbox.Processes;

namespace AgentSandbox.Tests;

/// <summary>Answers commands by their exact command line and records every call; an unexpected command fails the test.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
	private readonly Dictionary<string, Func<ProcessSpec, ProcessResult>> _handlers = [];

	public List<(ProcessSpec Spec, bool Interactive)> Calls { get; } = [];

	public IEnumerable<string> CommandLines => Calls.Select(c => c.Spec.CommandLine);

	public FakeProcessRunner On(string commandLine, Func<ProcessSpec, ProcessResult> handler)
	{
		_handlers[commandLine] = handler;
		return this;
	}

	public FakeProcessRunner On(string commandLine, ProcessResult result) => On(commandLine, _ => result);

	public FakeProcessRunner On(string commandLine, int exitCode, string stdOut = "") =>
		On(commandLine, new ProcessResult(exitCode, stdOut));

	public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken) =>
		Task.FromResult(Handle(spec, interactive: false));

	public Task<int> RunInteractiveAsync(ProcessSpec spec, CancellationToken cancellationToken) =>
		Task.FromResult(Handle(spec, interactive: true).ExitCode);

	private ProcessResult Handle(ProcessSpec spec, bool interactive)
	{
		Calls.Add((spec, interactive));
		return _handlers.TryGetValue(spec.CommandLine, out var handler)
			? handler(spec)
			: throw new InvalidOperationException($"unexpected command: {spec.CommandLine}");
	}
}
