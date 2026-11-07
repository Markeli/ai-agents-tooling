using System.Text.RegularExpressions;
using AgentSandbox.Platform;
using AgentSandbox.Processes;

namespace AgentSandbox;

/// <summary>Creates (once) and attaches to a clone-mode sandbox for a repository, running Claude Code as markeli-agent.</summary>
internal sealed partial class Launcher(IProcessRunner runner, ISecretStore secrets, IBotSshAgent sshAgent, TextWriter output)
{
	private static readonly (string Service, string KeychainItem)[] Secrets =
	[
		("github", "agent/personal-github"),
		("gitlab", "agent/personal-gitlab"),
	];

	public async Task<int> RunAsync(LauncherOptions options, LauncherSettings settings, CancellationToken cancellationToken)
	{
		var repo = await ResolveRepositoryAsync(options.RepoPath, cancellationToken);
		var name = options.Name ?? DeriveName(repo);
		if (!ValidName().IsMatch(name))
		{
			throw new LauncherException(
				$"invalid sandbox name '{name}': use lowercase letters, digits and hyphens, starting with a letter or digit");
		}

		await EnsureBotAgentAsync(settings, cancellationToken);

		if (options.Recreate && await SandboxExistsAsync(name, cancellationToken))
		{
			await RecreateAsync(repo, name, options.Yes, cancellationToken);
		}

		if (!await SandboxExistsAsync(name, cancellationToken))
		{
			// Sandbox-scoped secrets can exist before the sandbox; the kit's first-run approval then finds them
			foreach (var (service, item) in Secrets)
			{
				await BindSecretAsync(name, service, item, cancellationToken);
			}

			var kit = await settings.ResolveKit(cancellationToken);
			await RunInteractiveOrExitAsync(
				Sbx("create", "--clone", "--pull", settings.Pull, "--name", name, kit, repo),
				cancellationToken);
		}

		await PrintImageAsync(name, cancellationToken);

		if (options.NoAttach)
		{
			await output.WriteLineAsync($"agent-sandbox: {name} ready (not attached)");
			return 0;
		}

		string[] run = options.AgentArguments.Count == 0
			? ["run", "--name", name]
			: ["run", "--name", name, "--", .. options.AgentArguments];
		return await runner.RunInteractiveAsync(Sbx(run), cancellationToken);
	}

	/// <summary>The repository directory name, lowercased, with dots and underscores turned into hyphens.</summary>
	internal static string DeriveName(string repo) =>
		Path.GetFileName(repo.TrimEnd('/', '\\')).ToLowerInvariant().Replace('.', '-').Replace('_', '-');

	private async Task<string> ResolveRepositoryAsync(string path, CancellationToken cancellationToken)
	{
		var result = await runner.RunAsync(
			new ProcessSpec("git", ["-C", path, "rev-parse", "--show-toplevel"]) { StdErr = OutputMode.Inherit },
			cancellationToken);
		return result.Succeeded ? result.StdOut.TrimEnd('\r', '\n') : throw LauncherException.ExitWith(result.ExitCode);
	}

	private async Task EnsureBotAgentAsync(LauncherSettings settings, CancellationToken cancellationToken)
	{
		await sshAgent.EnsureLoadedAsync(settings.SshSocket, settings.SshKey, cancellationToken);
		var configured = await runner.RunAsync(Sbx("settings", "get", "ssh.agentSocketPath"), cancellationToken);
		var socket = configured.Succeeded ? configured.StdOut.TrimEnd('\r', '\n') : string.Empty;
		if (socket != settings.SshSocket)
		{
			throw new LauncherException(
				$"sbx forwards '{socket}', expected '{settings.SshSocket}': "
				+ $"run sbx settings set ssh.agentSocketPath {settings.SshSocket} && sbx daemon restart");
		}
	}

	private async Task BindSecretAsync(string name, string service, string item, CancellationToken cancellationToken)
	{
		if (!await secrets.ContainsAsync(item, cancellationToken))
		{
			throw new LauncherException($"keychain item {item} not found");
		}

		// sbx reports success on stderr with colour codes that come out garbled when stdout isn't a terminal: show it only on failure
		var result = await runner.RunAsync(
			Sbx("secret", "set", service, "--sandbox", name, "--command", secrets.LookupCommand(item)),
			cancellationToken);
		if (!result.Succeeded)
		{
			throw new LauncherException($"cannot set secret {service} for {name}: {result.StdOut}{result.StdErr}".TrimEnd());
		}
	}

	private async Task<bool> SandboxExistsAsync(string name, CancellationToken cancellationToken)
	{
		var result = await runner.RunAsync(Sbx("ls"), cancellationToken);
		return result.Succeeded
			&& result.StdOut
				.Split('\n')
				.Skip(1)
				.Select(line => line.Split((char[])[' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
				.Contains(name);
	}

	// Clone mode adds a "sandbox-<name>" git remote to the host repo, served by a git-daemon inside the sandbox
	// (see sandbox/README.md). That lets us check for commits the sandbox has that origin doesn't, without any
	// access to the sandbox itself. It cannot see uncommitted/staged-but-not-committed changes in the sandbox's
	// working tree (git-daemon only serves refs) — recreate does not guard against losing those.
	private async Task RecreateAsync(string repo, string name, bool yes, CancellationToken cancellationToken)
	{
		var remote = $"sandbox-{name}";

		// The remote (and its git-daemon) only exists while the sandbox is running, so a stopped sandbox must be started
		// before it can be fetched. sbx exec starts a stopped sandbox as a side effect of running a command in it.
		var start = await runner.RunAsync(Sbx("exec", name, "true") with { StdErr = OutputMode.Inherit }, cancellationToken);
		if (!start.Succeeded)
		{
			throw new LauncherException($"cannot start {remote}");
		}

		var fetch = await runner.RunAsync(
			new ProcessSpec("git", ["-C", repo, "fetch", remote, "--quiet"]) { StdErr = OutputMode.Inherit },
			cancellationToken);
		if (!fetch.Succeeded)
		{
			throw new LauncherException($"cannot fetch {remote}; is the sandbox still running? (sbx ls)");
		}

		var compare = await runner.RunAsync(
			new ProcessSpec("git", ["-C", repo, "rev-list", $"--remotes={remote}", "--not", "--remotes=origin"])
			{
				StdErr = OutputMode.Inherit,
			},
			cancellationToken);
		if (!compare.Succeeded)
		{
			throw new LauncherException($"cannot compare {remote} against origin");
		}

		var unpushed = compare.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
		if (unpushed is not null)
		{
			throw new LauncherException(
				$"{remote} has commits not pushed to origin (e.g. {unpushed}); push them first, then retry --recreate");
		}

		await RunInteractiveOrExitAsync(yes ? Sbx("rm", "--force", name) : Sbx("rm", name), cancellationToken);
	}

	private async Task PrintImageAsync(string name, CancellationToken cancellationToken)
	{
		var inspect = await runner.RunAsync(Sbx("inspect", name, "--json"), cancellationToken);
		var image = inspect.Succeeded ? SandboxImage.DescribeReference(inspect.StdOut) : SandboxImage.Unknown;

		// Images from before 1.0.0 have no image-release file
		var release = await runner.RunAsync(
			Sbx("exec", name, "sh", "-c", "cat /etc/claude-code/image-release 2>/dev/null || echo VERSION=unversioned"),
			cancellationToken);
		var version = release.Succeeded ? SandboxImage.ParseVersion(release.StdOut) : SandboxImage.Unknown;

		await output.WriteLineAsync($"agent-sandbox: {name} runs {image}, version {version}");
	}

	private async Task RunInteractiveOrExitAsync(ProcessSpec spec, CancellationToken cancellationToken)
	{
		var exitCode = await runner.RunInteractiveAsync(spec, cancellationToken);
		if (exitCode != 0)
		{
			throw LauncherException.ExitWith(exitCode);
		}
	}

	private static ProcessSpec Sbx(params string[] arguments) => new("sbx", arguments);

	[GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
	private static partial Regex ValidName();
}
