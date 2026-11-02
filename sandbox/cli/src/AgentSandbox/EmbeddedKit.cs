using System.Reflection;

namespace AgentSandbox;

/// <summary>The sbx kit (sandbox/kits/markeli-claude) embedded in the tool and written to disk for <c>sbx create</c>.</summary>
internal static class EmbeddedKit
{
	public const string Name = "markeli-claude";

	private const string ResourcePrefix = "kit/";

	/// <summary>
	/// Writes the kit under <c>&lt;dataDirectory&gt;/agent-sandbox/kits/&lt;version&gt;/</c> and returns its directory.
	/// Files that differ are rewritten, so a reinstalled build of the same version never runs with an older kit.
	/// </summary>
	public static string Extract(string dataDirectory, string version)
	{
		var root = Path.Combine(dataDirectory, "agent-sandbox", "kits", version);
		var assembly = typeof(EmbeddedKit).Assembly;
		foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
		{
			// MSBuild's %(RecursiveDir) uses the build host's directory separator
			var relative = resource[ResourcePrefix.Length..].Replace('\\', '/');
			var path = Path.Combine(root, relative);
			var content = ReadResource(assembly, resource);
			if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
			{
				continue;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllBytes(path, content);
		}

		return Path.Combine(root, Name);
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

	private static byte[] ReadResource(Assembly assembly, string resource)
	{
		using var stream = assembly.GetManifestResourceStream(resource)!;
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}
}
