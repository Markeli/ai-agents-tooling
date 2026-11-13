namespace AgentSandbox.Tests;

public sealed class SandboxImageTests
{
	[Theory]
	[InlineData("VERSION=1.0.1\nTREE=abc\nREVISION=fe57fa56ff3d9edbf9d3782ed535ded65838e550\n", "version 1.0.1, revision fe57fa5")]
	[InlineData("VERSION=1.0.1+unreleased.abcdef012345\r\nREVISION=abc\r\n", "version 1.0.1+unreleased.abcdef012345, revision abc")]
	[InlineData("VERSION=0.0.0+local\nTREE=unknown\nREVISION=unknown\n", "version 0.0.0+local")]
	[InlineData("VERSION=unversioned\n", "version unversioned")]
	[InlineData("TREE=abc\n", "version unknown")]
	[InlineData("VERSION=\nREVISION=\n", "version unknown")]
	[InlineData("", "version unknown")]
	public void Describes_the_release_from_image_release(string release, string description) =>
		SandboxImage.DescribeRelease(release).Should().Be(description);
}
