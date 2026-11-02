using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentSandbox;

/// <summary>Reads which image a sandbox runs; whatever can't be read shows as <c>unknown</c>.</summary>
internal static partial class SandboxImage
{
	public const string Unknown = "unknown";

	/// <summary>
	/// The image reference with its digest from <c>sbx inspect --json</c>. sbx doesn't document its field names, so this
	/// takes the first string that carries a digest, or else joins the first image-like field with the first digest.
	/// </summary>
	public static string DescribeReference(string inspectJson)
	{
		JsonNode? document;
		try
		{
			document = JsonNode.Parse(inspectJson);
		}
		catch (JsonException)
		{
			return Unknown;
		}

		var strings = Descend(document).Select(AsString).OfType<string>().ToList();
		var withDigest = strings.FirstOrDefault(s => ReferenceWithDigest().IsMatch(s));
		if (withDigest is not null)
		{
			return withDigest;
		}

		var image = Descend(document)
			.OfType<JsonObject>()
			.SelectMany(o => o)
			.Where(p => p.Key.Contains("image", StringComparison.OrdinalIgnoreCase))
			.Select(p => AsString(p.Value))
			.FirstOrDefault(s => s is not null && !s.StartsWith("sha256:", StringComparison.Ordinal));
		var digest = strings.FirstOrDefault(s => BareDigest().IsMatch(s));
		return image is null && digest is null ? Unknown : $"{image ?? Unknown}@{digest ?? Unknown}";
	}

	/// <summary>The <c>VERSION</c> from <c>/etc/claude-code/image-release</c>.</summary>
	public static string ParseVersion(string imageRelease)
	{
		var version = imageRelease
			.Split('\n')
			.Select(line => line.TrimEnd('\r'))
			.FirstOrDefault(line => line.StartsWith("VERSION=", StringComparison.Ordinal))?["VERSION=".Length..];
		return string.IsNullOrEmpty(version) ? Unknown : version;
	}

	// Pre-order, values in document order: the order of jq's `..`
	private static IEnumerable<JsonNode?> Descend(JsonNode? node)
	{
		yield return node;
		IEnumerable<JsonNode?> children = node switch
		{
			JsonObject obj => obj.Select(p => p.Value),
			JsonArray array => array,
			_ => [],
		};
		foreach (var descendant in children.SelectMany(Descend))
		{
			yield return descendant;
		}
	}

	private static string? AsString(JsonNode? node) =>
		node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

	[GeneratedRegex("@sha256:[0-9a-f]{64}")]
	private static partial Regex ReferenceWithDigest();

	[GeneratedRegex("^sha256:[0-9a-f]{64}$")]
	private static partial Regex BareDigest();
}
