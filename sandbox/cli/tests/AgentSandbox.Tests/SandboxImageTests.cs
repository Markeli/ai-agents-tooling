namespace AgentSandbox.Tests;

public sealed class SandboxImageTests
{
	private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

	[Fact]
	public void Takes_the_first_string_that_carries_a_digest() =>
		SandboxImage.DescribeReference(
			$$$"""{"name":"app","workspace":["/work/app"],"template":{"ref":"ghcr.io/markeli/claude-sandbox:latest@{{{Digest}}}"}}""")
			.Should().Be($"ghcr.io/markeli/claude-sandbox:latest@{Digest}");

	[Fact]
	public void Joins_an_image_field_with_a_separate_digest() =>
		SandboxImage.DescribeReference($$"""{"Image":"ghcr.io/markeli/claude-sandbox:latest","ImageID":"{{Digest}}"}""")
			.Should().Be($"ghcr.io/markeli/claude-sandbox:latest@{Digest}");

	[Fact]
	public void Marks_the_missing_half_as_unknown() =>
		SandboxImage.DescribeReference("""{"agent":{"image":"ghcr.io/markeli/claude-sandbox:1.0.1"}}""")
			.Should().Be("ghcr.io/markeli/claude-sandbox:1.0.1@unknown");

	[Theory]
	[InlineData("""{"name":"app"}""")]
	[InlineData("not json")]
	[InlineData("")]
	public void Is_unknown_when_nothing_matches(string json) =>
		SandboxImage.DescribeReference(json).Should().Be("unknown");

	[Theory]
	[InlineData("VERSION=1.0.1\nTREE=abc\nREVISION=def\n", "1.0.1")]
	[InlineData("VERSION=1.0.1+unreleased.abcdef012345\r\n", "1.0.1+unreleased.abcdef012345")]
	[InlineData("VERSION=unversioned\n", "unversioned")]
	[InlineData("TREE=abc\n", "unknown")]
	[InlineData("VERSION=\n", "unknown")]
	public void Reads_the_version_from_image_release(string release, string version) =>
		SandboxImage.ParseVersion(release).Should().Be(version);
}
