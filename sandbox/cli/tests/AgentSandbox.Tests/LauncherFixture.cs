using AgentSandbox.Platform;
using AgentSandbox.Processes;

namespace AgentSandbox.Tests;

/// <summary>A host where everything works: the repository /work/app, a running bot agent and both keychain items.</summary>
internal sealed class LauncherFixture
{
	public const string Repo = "/work/app";
	public const string Socket = "/home/owner/.ssh/markeli-agent.sock";
	public const string Kit = "/kits/markeli-claude";
	public const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

	public LauncherFixture(string name = "app")
	{
		Name = name;
		Runner
			.On($"git -C {Repo} rev-parse --show-toplevel", 0, $"{Repo}\n")
			.On("sbx settings get ssh.agentSocketPath", 0, $"{Socket}\n")
			.On("sbx ls", _ => new ProcessResult(0, Exists ? $"SANDBOX   AGENT\n{Name}   claude\n" : "SANDBOX   AGENT\n"))
			.On($"sbx secret set github --sandbox {Name} --command lookup agent/personal-github", 0)
			.On($"sbx secret set gitlab --sandbox {Name} --command lookup agent/personal-gitlab", 0)
			.On($"sbx create --clone --pull missing --name {Name} {Kit} {Repo}", _ =>
			{
				Exists = true;
				return new ProcessResult(0);
			})
			.On($"sbx inspect {Name} --json", 0, $$"""{"name":"{{Name}}","image":"ghcr.io/markeli/claude-sandbox:latest@{{Digest}}"}""")
			.On(
				$"sbx exec {Name} sh -c cat /etc/claude-code/image-release 2>/dev/null || echo VERSION=unversioned",
				0,
				"VERSION=1.0.1\nTREE=abc\n")
			.On($"sbx run --name {Name}", 0)
			.On($"sbx exec {Name} true", 0)
			.On($"git -C {Repo} fetch sandbox-{Name} --quiet", 0)
			.On($"git -C {Repo} rev-list --remotes=sandbox-{Name} --not --remotes=origin", 0)
			.On($"sbx rm {Name}", RemoveSandbox)
			.On($"sbx rm --force {Name}", RemoveSandbox);
	}

	public string Name { get; }

	public bool Exists { get; set; }

	public FakeProcessRunner Runner { get; } = new();

	public FakeSecretStore Secrets { get; } = new("agent/personal-github", "agent/personal-gitlab");

	public FakeBotSshAgent SshAgent { get; } = new();

	public StringWriter Output { get; } = new();

	public LauncherSettings Settings { get; set; } = new(Socket, "/home/owner/.ssh/markeli_agent", "missing", () => Kit);

	public Task<int> RunAsync(LauncherOptions options) =>
		new Launcher(Runner, Secrets, SshAgent, Output).RunAsync(options, Settings, CancellationToken.None);

	private ProcessResult RemoveSandbox(ProcessSpec spec)
	{
		Exists = false;
		return new ProcessResult(0);
	}
}

internal sealed class FakeSecretStore(params string[] items) : ISecretStore
{
	public HashSet<string> Items { get; } = [.. items];

	public Task<bool> ContainsAsync(string item, CancellationToken cancellationToken) => Task.FromResult(Items.Contains(item));

	public string LookupCommand(string item) => $"lookup {item}";
}

internal sealed class FakeBotSshAgent : IBotSshAgent
{
	public List<(string Socket, string Key)> Loads { get; } = [];

	public Exception? Failure { get; set; }

	public Task EnsureLoadedAsync(string socket, string keyPath, CancellationToken cancellationToken)
	{
		Loads.Add((socket, keyPath));
		return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
	}
}
