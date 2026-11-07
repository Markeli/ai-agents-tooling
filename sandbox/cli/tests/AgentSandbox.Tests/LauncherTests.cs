using AgentSandbox.Processes;

namespace AgentSandbox.Tests;

public sealed class LauncherTests
{
	private static readonly LauncherOptions Defaults = new() { RepoPath = LauncherFixture.Repo };

	[Fact]
	public async Task Creates_a_missing_sandbox_with_the_bot_secrets_then_attaches()
	{
		var fixture = new LauncherFixture();

		var exitCode = await fixture.RunAsync(Defaults);

		exitCode.Should().Be(0);
		fixture.SshAgent.Loads.Should().Equal((LauncherFixture.Socket, "/home/owner/.ssh/markeli_agent"));
		fixture.Runner.CommandLines.Should().Equal(
			"git -C /work/app rev-parse --show-toplevel",
			"sbx settings get ssh.agentSocketPath",
			"sbx ls",
			"sbx secret set github --sandbox app --command lookup agent/personal-github",
			"sbx secret set gitlab --sandbox app --command lookup agent/personal-gitlab",
			"sbx create --clone --pull missing --name app /kits/markeli-claude /work/app",
			"sbx inspect app --json",
			"sbx exec app sh -c cat /etc/claude-code/image-release 2>/dev/null || echo VERSION=unversioned",
			"sbx run --name app");
		fixture.Runner.Calls.Where(c => c.Interactive).Select(c => c.Spec.CommandLine).Should().Equal(
			"sbx create --clone --pull missing --name app /kits/markeli-claude /work/app",
			"sbx run --name app");
		fixture.Output.ToString().Should().Be(
			$"agent-sandbox: app runs ghcr.io/markeli/claude-sandbox:latest@{LauncherFixture.Digest}, version 1.0.1\n");
	}

	[Fact]
	public async Task Attaches_to_an_existing_sandbox_without_creating_it_and_passes_the_agent_arguments()
	{
		var fixture = new LauncherFixture { Exists = true };
		fixture.Runner.On("sbx run --name app -- -c --model opus", 0);
		var kitResolved = false;
		fixture.Settings = fixture.Settings with { ResolveKit = _ => { kitResolved = true; return Task.FromResult(LauncherFixture.Kit); } };

		var exitCode = await fixture.RunAsync(Defaults with { AgentArguments = ["-c", "--model", "opus"] });

		exitCode.Should().Be(0);
		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx create") || c.StartsWith("sbx secret"));
		fixture.Runner.CommandLines.Last().Should().Be("sbx run --name app -- -c --model opus");
		kitResolved.Should().BeFalse("the kit is only needed to create a sandbox");
	}

	[Fact]
	public async Task Returns_the_exit_code_of_the_attached_session()
	{
		var fixture = new LauncherFixture { Exists = true };
		fixture.Runner.On("sbx run --name app", 130);

		(await fixture.RunAsync(Defaults)).Should().Be(130);
	}

	[Fact]
	public async Task No_attach_reports_ready_instead_of_attaching()
	{
		var fixture = new LauncherFixture();

		var exitCode = await fixture.RunAsync(Defaults with { NoAttach = true });

		exitCode.Should().Be(0);
		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx run"));
		fixture.Output.ToString().Should().EndWith("agent-sandbox: app ready (not attached)\n");
	}

	[Fact]
	public async Task Uses_the_pull_policy_and_kit_from_the_settings()
	{
		var fixture = new LauncherFixture();
		fixture.Settings = fixture.Settings with { Pull = "always", ResolveKit = _ => Task.FromResult("/tmp/my-kit") };
		fixture.Runner.On("sbx create --clone --pull always --name app /tmp/my-kit /work/app", _ =>
		{
			fixture.Exists = true;
			return new ProcessResult(0);
		});

		(await fixture.RunAsync(Defaults)).Should().Be(0);

		fixture.Runner.CommandLines.Should().Contain("sbx create --clone --pull always --name app /tmp/my-kit /work/app");
	}

	[Theory]
	[InlineData("/work/My.Repo_x", "my-repo-x")]
	[InlineData("/work/app/", "app")]
	[InlineData("/work/markeli.github.io", "markeli-github-io")]
	public void Derives_the_name_from_the_repository_directory(string repo, string name) =>
		Launcher.DeriveName(repo).Should().Be(name);

	[Fact]
	public async Task Uses_the_given_name_everywhere()
	{
		var fixture = new LauncherFixture("blog-2");

		(await fixture.RunAsync(Defaults with { Name = "blog-2" })).Should().Be(0);

		fixture.Runner.CommandLines.Should().Contain("sbx create --clone --pull missing --name blog-2 /kits/markeli-claude /work/app");
		fixture.Runner.CommandLines.Last().Should().Be("sbx run --name blog-2");
	}

	[Theory]
	[InlineData("Blog")]
	[InlineData("-blog")]
	[InlineData("blog_1")]
	public async Task Rejects_an_invalid_name_before_touching_sbx(string name)
	{
		var fixture = new LauncherFixture(name);

		var act = () => fixture.RunAsync(Defaults with { Name = name });

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be(
			$"invalid sandbox name '{name}': use lowercase letters, digits and hyphens, starting with a letter or digit");
		fixture.Runner.CommandLines.Should().Equal("git -C /work/app rev-parse --show-toplevel");
		fixture.SshAgent.Loads.Should().BeEmpty();
	}

	[Fact]
	public async Task Exits_with_gits_exit_code_when_the_path_is_not_a_repository()
	{
		var fixture = new LauncherFixture();
		fixture.Runner.On("git -C /elsewhere rev-parse --show-toplevel", 128);

		var act = () => fixture.RunAsync(Defaults with { RepoPath = "/elsewhere" });

		var error = (await act.Should().ThrowAsync<LauncherException>()).Which;
		error.ExitCode.Should().Be(128);
		error.HasMessage.Should().BeFalse("git already printed why");
		fixture.Runner.Calls.Single().Spec.StdErr.Should().Be(OutputMode.Inherit);
	}

	[Fact]
	public async Task Stops_when_sbx_forwards_another_agent_socket()
	{
		var fixture = new LauncherFixture();
		fixture.Runner.On("sbx settings get ssh.agentSocketPath", 0, "/other.sock\n");

		var act = () => fixture.RunAsync(Defaults);

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be(
			$"sbx forwards '/other.sock', expected '{LauncherFixture.Socket}': "
			+ $"run sbx settings set ssh.agentSocketPath {LauncherFixture.Socket} && sbx daemon restart");
	}

	[Fact]
	public async Task Treats_a_failing_settings_lookup_as_no_socket()
	{
		var fixture = new LauncherFixture();
		fixture.Runner.On("sbx settings get ssh.agentSocketPath", 1, LauncherFixture.Socket);

		var act = () => fixture.RunAsync(Defaults);

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().StartWith("sbx forwards '',");
	}

	[Fact]
	public async Task Stops_when_the_bot_agent_cannot_be_loaded()
	{
		var fixture = new LauncherFixture();
		fixture.SshAgent.Failure = new LauncherException("cannot load key");

		var act = () => fixture.RunAsync(Defaults);

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be("cannot load key");
		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx"));
	}

	[Fact]
	public async Task Stops_when_a_keychain_item_is_missing()
	{
		var fixture = new LauncherFixture();
		fixture.Secrets.Items.Remove("agent/personal-gitlab");

		var act = () => fixture.RunAsync(Defaults);

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be(
			"keychain item agent/personal-gitlab not found");
		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx create"));
	}

	[Fact]
	public async Task Keeps_the_output_of_sbx_secret_set_out_of_the_log_unless_it_fails()
	{
		var fixture = new LauncherFixture();
		fixture.Runner.On(
			"sbx secret set gitlab --sandbox app --command lookup agent/personal-gitlab",
			new ProcessResult(1, StdErr: "boom\n"));

		var act = () => fixture.RunAsync(Defaults);

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be("cannot set secret gitlab for app: boom");
		fixture.Runner.Calls
			.Where(c => c.Spec.FileName == "sbx" && c.Spec.Arguments[0] == "secret")
			.Should().OnlyContain(c => c.Spec.StdOut == OutputMode.Capture && c.Spec.StdErr == OutputMode.Capture);
	}

	[Fact]
	public async Task Exits_with_the_exit_code_of_a_failed_create()
	{
		var fixture = new LauncherFixture();
		fixture.Runner.On("sbx create --clone --pull missing --name app /kits/markeli-claude /work/app", 2);

		var act = () => fixture.RunAsync(Defaults);

		(await act.Should().ThrowAsync<LauncherException>()).Which.ExitCode.Should().Be(2);
		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx run"));
	}

	[Fact]
	public async Task Shows_unknown_when_the_image_cannot_be_read()
	{
		var fixture = new LauncherFixture { Exists = true };
		fixture.Runner
			.On("sbx inspect app --json", 1)
			.On("sbx exec app sh -c cat /etc/claude-code/image-release 2>/dev/null || echo VERSION=unversioned", 1);

		(await fixture.RunAsync(Defaults)).Should().Be(0);

		fixture.Output.ToString().Should().Be("agent-sandbox: app runs unknown, version unknown\n");
	}

	[Fact]
	public async Task Recreate_removes_a_sandbox_without_unpushed_commits_and_creates_it_again()
	{
		var fixture = new LauncherFixture { Exists = true };

		(await fixture.RunAsync(Defaults with { Recreate = true })).Should().Be(0);

		fixture.Runner.CommandLines.Should().ContainInOrder(
			"sbx ls",
			"sbx exec app true",
			"git -C /work/app fetch sandbox-app --quiet",
			"git -C /work/app rev-list --remotes=sandbox-app --not --remotes=origin",
			"sbx rm app",
			"sbx ls",
			"sbx create --clone --pull missing --name app /kits/markeli-claude /work/app");
		fixture.Runner.Calls.Should().Contain(c => c.Spec.CommandLine == "sbx rm app" && c.Interactive);
	}

	[Fact]
	public async Task Recreate_with_yes_forces_the_removal()
	{
		var fixture = new LauncherFixture { Exists = true };

		(await fixture.RunAsync(Defaults with { Recreate = true, Yes = true })).Should().Be(0);

		fixture.Runner.CommandLines.Should().Contain("sbx rm --force app").And.NotContain("sbx rm app");
	}

	[Fact]
	public async Task Recreate_of_a_missing_sandbox_just_creates_it()
	{
		var fixture = new LauncherFixture();

		(await fixture.RunAsync(Defaults with { Recreate = true })).Should().Be(0);

		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx exec app true") || c.StartsWith("sbx rm"));
		fixture.Runner.CommandLines.Should().Contain(c => c.StartsWith("sbx create"));
	}

	[Fact]
	public async Task Recreate_refuses_while_the_sandbox_has_unpushed_commits()
	{
		var fixture = new LauncherFixture { Exists = true };
		fixture.Runner.On("git -C /work/app rev-list --remotes=sandbox-app --not --remotes=origin", 0, "abc123\ndef456\n");

		var act = () => fixture.RunAsync(Defaults with { Recreate = true });

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be(
			"sandbox-app has commits not pushed to origin (e.g. abc123); push them first, then retry --recreate");
		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx rm"));
	}

	[Theory]
	[InlineData("sbx exec app true", "cannot start sandbox-app")]
	[InlineData("git -C /work/app fetch sandbox-app --quiet", "cannot fetch sandbox-app; is the sandbox still running? (sbx ls)")]
	[InlineData("git -C /work/app rev-list --remotes=sandbox-app --not --remotes=origin", "cannot compare sandbox-app against origin")]
	public async Task Recreate_stops_when_the_unpushed_check_cannot_run(string failing, string message)
	{
		var fixture = new LauncherFixture { Exists = true };
		fixture.Runner.On(failing, 1);

		var act = () => fixture.RunAsync(Defaults with { Recreate = true });

		(await act.Should().ThrowAsync<LauncherException>()).Which.Message.Should().Be(message);
		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx rm"));
	}

	[Fact]
	public async Task Recreate_stops_when_sbx_rm_is_declined_or_fails()
	{
		var fixture = new LauncherFixture { Exists = true };
		fixture.Runner.On("sbx rm app", 1);

		var act = () => fixture.RunAsync(Defaults with { Recreate = true });

		(await act.Should().ThrowAsync<LauncherException>()).Which.ExitCode.Should().Be(1);
		fixture.Runner.CommandLines.Should().NotContain(c => c.StartsWith("sbx create"));
	}
}
