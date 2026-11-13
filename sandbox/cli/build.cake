// Build, test, pack and install the agent-sandbox tool (docs/adr/0003-port-agent-sandbox-to-a-dotnet-tool.md).
// Task names follow the Curiosus curiosus.cake. Run from sandbox/cli: dotnet tool restore && dotnet cake --target=<Task>

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

const string Solution = "./AgentSandbox.slnx";
const string ToolProject = "./src/AgentSandbox/AgentSandbox.csproj";
const string PackageId = "AgentSandbox";
var artifacts = MakeAbsolute(Directory("./artifacts"));

Task("Clean")
	.Does(() =>
	{
		CleanDirectory(artifacts);
		DotNetClean(Solution, new DotNetCleanSettings { Configuration = configuration });
	});

Task("Build")
	.IsDependentOn("Clean")
	.Does(() => DotNetBuild(Solution, new DotNetBuildSettings { Configuration = configuration }));

// global.json selects Microsoft.Testing.Platform, whose dotnet test takes the solution as --solution
Task("Test")
	.IsDependentOn("Build")
	.Does(() => Dotnet("test", "--solution", Solution, "--configuration", configuration, "--no-build"));

Task("Default")
	.IsDependentOn("Build")
	.IsDependentOn("Test");

Task("Format")
	.Does(() => DotNetFormat(Solution, new DotNetFormatSettings { VerifyNoChanges = true }));

Task("Pack")
	.IsDependentOn("Default")
	.Does(() => DotNetPack(ToolProject, new DotNetPackSettings
	{
		Configuration = configuration,
		NoBuild = true,
		OutputDirectory = artifacts,
	}));

// Uninstalls first: dotnet tool update keeps an installed package of the same version, so a rebuilt 0.1.0 with a
// changed kit would never reach the host. Before that the package is installed into a scratch directory, so one that
// can't be installed doesn't cost the host its working agent-sandbox. The version is pinned so that no other source
// can supply the package.
Task("Install")
	.IsDependentOn("Pack")
	.Does(() =>
	{
		var version = XmlPeek(ToolProject, "/Project/PropertyGroup/Version");
		var trial = artifacts.Combine("install-check");
		Dotnet("tool", "install", PackageId, "--tool-path", trial.FullPath, "--add-source", artifacts.FullPath, "--version", version);
		DeleteDirectory(trial, new DeleteDirectorySettings { Recursive = true, Force = true });

		Dotnet(quiet: true, "tool", "uninstall", "--global", PackageId);
		Dotnet("tool", "install", "--global", PackageId, "--add-source", artifacts.FullPath, "--version", version);
	});

RunTarget(target);

void Dotnet(params string[] arguments) => Dotnet(quiet: false, arguments);

// quiet: ignore the exit code and the output, e.g. uninstalling a tool that isn't installed
void Dotnet(bool quiet, params string[] arguments)
{
	var builder = new ProcessArgumentBuilder();
	foreach (var argument in arguments)
	{
		builder.AppendQuoted(argument);
	}

	var settings = new ProcessSettings { Arguments = builder, RedirectStandardOutput = quiet, RedirectStandardError = quiet };
	var exitCode = StartProcess("dotnet", settings);
	if (exitCode != 0 && !quiet)
	{
		throw new CakeException($"dotnet {string.Join(' ', arguments)} exited with {exitCode}");
	}
}
