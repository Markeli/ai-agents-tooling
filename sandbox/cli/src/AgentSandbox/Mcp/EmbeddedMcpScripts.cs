namespace AgentSandbox.Mcp;

/// <summary>The MCP server launchers under plugins/mcp-servers embedded in the tool, written to disk for the host.</summary>
internal static class EmbeddedMcpScripts
{
	public const string KeychainEnv = "keychain-env";

	public const string GrafanaMcp = "grafana-mcp";

	private const string ResourcePrefix = "mcp/";

	private const UnixFileMode Executable =
		UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
		| UnixFileMode.GroupRead | UnixFileMode.GroupExecute
		| UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

	/// <summary>The names of the embedded scripts, sorted.</summary>
	public static IReadOnlyList<string> Names { get; } = typeof(EmbeddedMcpScripts).Assembly.GetManifestResourceNames()
		.Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
		.Select(n => n[ResourcePrefix.Length..])
		.Order(StringComparer.Ordinal)
		.ToArray();

	/// <summary>
	/// Writes every script, mode 0755, to <c>&lt;dataDirectory&gt;/agent-sandbox/mcp/&lt;version&gt;/</c>. Scripts that
	/// differ are rewritten, files the tool no longer has are deleted, and the directories of other tool versions are
	/// removed.
	/// </summary>
	/// <returns>The directory, and whether anything in it changed.</returns>
	public static async Task<(string Directory, bool Changed)> ExtractAsync(
		string dataDirectory,
		string version,
		CancellationToken cancellationToken)
	{
		var parent = Path.Combine(dataDirectory, "agent-sandbox", "mcp");
		var directory = Path.Combine(parent, version);
		Directory.CreateDirectory(directory);

		var changed = false;
		foreach (var name in Names)
		{
			var path = Path.Combine(directory, name);
			var content = await EmbeddedKits.ReadResourceAsync(ResourcePrefix + name, cancellationToken);
			if (File.Exists(path) && (await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(content))
			{
				if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != Executable)
				{
					File.SetUnixFileMode(path, Executable);
					changed = true;
				}

				continue;
			}

			// The temporary file goes to the parent: the version directory keeps only the scripts
			await EmbeddedKits.ReplaceAsync(path, content, temporaryDirectory: parent, cancellationToken, Executable);
			changed = true;
		}

		foreach (var stale in Directory.GetFileSystemEntries(directory).Where(e => !Names.Contains(Path.GetFileName(e))))
		{
			if (Directory.Exists(stale) && new DirectoryInfo(stale).LinkTarget is null)
			{
				Directory.Delete(stale, recursive: true);
			}
			else
			{
				File.Delete(stale);
			}

			changed = true;
		}

		EmbeddedKits.DeleteOtherVersions(directory);
		return (directory, changed);
	}
}
