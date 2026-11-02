using AgentSandbox.Processes;

namespace AgentSandbox.Platform;

/// <summary>Generic passwords in the macOS login keychain, looked up by service name.</summary>
internal sealed class MacKeychainSecretStore(IProcessRunner runner) : ISecretStore
{
	private const string Security = "/usr/bin/security";

	public async Task<bool> ContainsAsync(string item, CancellationToken cancellationToken)
	{
		var result = await runner.RunAsync(new ProcessSpec(Security, ["find-generic-password", "-s", item]), cancellationToken);
		return result.Succeeded;
	}

	public string LookupCommand(string item) => $"{Security} find-generic-password -s {item} -w";
}
