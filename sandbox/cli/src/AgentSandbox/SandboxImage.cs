namespace AgentSandbox;

/// <summary>The toolchain image sandboxes are created from, and what a sandbox says about the one it runs.</summary>
internal static class SandboxImage
{
	/// <summary>The image <c>sbx create --template</c> gets unless <c>AGENT_SANDBOX_IMAGE</c> names another.</summary>
	public const string Default = "ghcr.io/markeli/claude-sandbox:latest";

	public const string Unknown = "unknown";

	/// <summary>
	/// The release in <c>/etc/claude-code/image-release</c> as <c>version 1.0.1, revision 0123456</c>; the revision
	/// is left out when the file has none. What can't be read shows as <c>unknown</c>.
	/// </summary>
	/// <remarks>
	/// The file is the only place that names the image: a sandbox made of v3 kits runs an image sbx assembles, and
	/// <c>sbx inspect</c> reports that one (<c>sandbox-kit-assembled:&lt;hash&gt;</c>), not the template it started from.
	/// </remarks>
	public static string DescribeRelease(string imageRelease)
	{
		var version = Value(imageRelease, "VERSION") ?? Unknown;
		var revision = Value(imageRelease, "REVISION");
		return revision is null or Unknown ? $"version {version}" : $"version {version}, revision {Abbreviate(revision)}";
	}

	private static string? Value(string imageRelease, string key) =>
		imageRelease
			.Split('\n')
			.Select(line => line.TrimEnd('\r'))
			.FirstOrDefault(line => line.StartsWith($"{key}=", StringComparison.Ordinal))?[(key.Length + 1)..] is { Length: > 0 } value
			? value
			: null;

	private static string Abbreviate(string revision) => revision.Length > 7 ? revision[..7] : revision;
}
