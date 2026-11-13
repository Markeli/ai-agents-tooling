using System.Reflection;

namespace AgentSandbox;

/// <summary>The sbx kits (every directory under sandbox/kits) embedded in the tool and written to disk for <c>sbx create</c>.</summary>
internal static class EmbeddedKits
{
	/// <summary>The kit the launcher uses until kits become configurable (#31).</summary>
	public const string Default = "markeli-claude";

	private const string ResourcePrefix = "kits/";

	/// <summary>The tool's package version, which names the directory the kits are written to.</summary>
	public static string ToolVersion { get; } =
		typeof(EmbeddedKits).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? "0.0.0";

	/// <summary>The names of the embedded kits, sorted.</summary>
	public static IReadOnlyList<string> Names { get; } = Resources()
		.Select(r => r.Path.Split('/')[0])
		.Distinct(StringComparer.Ordinal)
		.Order(StringComparer.Ordinal)
		.ToArray();

	/// <summary>
	/// Writes the kit <paramref name="kit"/> under <c>&lt;dataDirectory&gt;/agent-sandbox/kits/&lt;version&gt;/</c> and
	/// returns its directory. Files that differ are rewritten and files the kit no longer has are deleted, so a
	/// reinstalled build of the same version never runs with an older kit. The kits of other tool versions are removed.
	/// </summary>
	/// <remarks>
	/// Safe for launchers started side by side: each file is renamed into place, so a reader sees it whole.
	/// </remarks>
	/// <exception cref="LauncherException">The tool has no kit named <paramref name="kit"/>.</exception>
	public static async Task<string> ExtractAsync(
		string kit,
		string dataDirectory,
		string version,
		CancellationToken cancellationToken)
	{
		var files = Resources().Where(r => r.Path.StartsWith($"{kit}/", StringComparison.Ordinal)).ToArray();
		if (files.Length == 0)
		{
			throw new LauncherException($"no kit named '{kit}'; the tool has: {string.Join(", ", Names)}");
		}

		var root = Path.Combine(dataDirectory, "agent-sandbox", "kits", version);
		var directory = Path.Combine(root, kit);

		// Ignoring case: on a case-insensitive file system a kit file renamed only in case keeps its old name on disk
		var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (resource, relative) in files)
		{
			var path = Path.GetFullPath(Path.Combine(root, relative));
			written.Add(path);
			var content = await ReadResourceAsync(resource, cancellationToken);
			if (File.Exists(path) && (await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(content))
			{
				continue;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			await ReplaceAsync(path, content, temporaryDirectory: root, cancellationToken);
		}

		foreach (var stale in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(f => !written.Contains(f)))
		{
			File.Delete(stale);
		}

		DeleteOtherVersions(root);
		return directory;
	}

	/// <summary>Per-user data directory: <c>$XDG_DATA_HOME</c> or <c>~/.local/share</c>; local app data on Windows.</summary>
	public static string DefaultDataDirectory(Func<string, string?> getEnvironmentVariable, string home)
	{
		if (OperatingSystem.IsWindows())
		{
			return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		}

		var xdg = getEnvironmentVariable("XDG_DATA_HOME");
		return string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg;
	}

	// Written beside the kit, not inside it, where a launcher running in parallel would delete it as a stale file
	private static async Task ReplaceAsync(
		string path,
		byte[] content,
		string temporaryDirectory,
		CancellationToken cancellationToken)
	{
		var temporary = Path.Combine(temporaryDirectory, $".{Guid.NewGuid():N}.tmp");
		try
		{
			await File.WriteAllBytesAsync(temporary, content, cancellationToken);
			File.Move(temporary, path, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	// Housekeeping only: a directory that can't be removed now goes on the next run
	private static void DeleteOtherVersions(string root)
	{
		foreach (var other in Directory.GetDirectories(Path.GetDirectoryName(root)!).Where(d => d != root))
		{
			try
			{
				Directory.Delete(other, recursive: true);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
			}
		}
	}

	// Path is relative to sandbox/kits, with '/': MSBuild's %(RecursiveDir) uses the build host's directory separator
	private static IEnumerable<(string Resource, string Path)> Resources() =>
		typeof(EmbeddedKits).Assembly.GetManifestResourceNames()
			.Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
			.Select(n => (n, n[ResourcePrefix.Length..].Replace('\\', '/')));

	private static async Task<byte[]> ReadResourceAsync(string resource, CancellationToken cancellationToken)
	{
		await using var stream = typeof(EmbeddedKits).Assembly.GetManifestResourceStream(resource)!;
		using var buffer = new MemoryStream();
		await stream.CopyToAsync(buffer, cancellationToken);
		return buffer.ToArray();
	}
}
