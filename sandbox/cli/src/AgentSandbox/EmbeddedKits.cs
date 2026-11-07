namespace AgentSandbox;

/// <summary>The sbx kits (every directory under sandbox/kits) embedded in the tool and written to disk for <c>sbx create</c>.</summary>
internal static class EmbeddedKits
{
	/// <summary>The kit the launcher uses until kits become configurable (#31).</summary>
	public const string Default = "markeli-claude";

	private const string ResourcePrefix = "kits/";

	/// <summary>The names of the embedded kits, sorted.</summary>
	public static IReadOnlyList<string> Names { get; } = Resources()
		.Select(r => r.Path.Split('/')[0])
		.Distinct(StringComparer.Ordinal)
		.Order(StringComparer.Ordinal)
		.ToArray();

	/// <summary>
	/// Writes the kit <paramref name="kit"/> under <c>&lt;dataDirectory&gt;/agent-sandbox/kits/&lt;version&gt;/</c> and
	/// returns its directory. Files that differ are rewritten, so a reinstalled build of the same version never runs with
	/// an older kit.
	/// </summary>
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
		foreach (var (resource, relative) in files)
		{
			var path = Path.Combine(root, relative);
			var content = await ReadResourceAsync(resource, cancellationToken);
			if (File.Exists(path) && (await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(content))
			{
				continue;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			await File.WriteAllBytesAsync(path, content, cancellationToken);
		}

		return Path.Combine(root, kit);
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
