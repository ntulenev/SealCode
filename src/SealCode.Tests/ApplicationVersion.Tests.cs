using System.Reflection;
using System.Reflection.Emit;

using FluentAssertions;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class ApplicationVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3", null)]
    [InlineData("1.2.3+sha", "1.2.3", "sha")]
    [InlineData("1.2.3+a.b.sha", "1.2.3", "sha")]
    public void FromShouldExtractVersionAndLastCommitSegment(string informational, string version, string? commit)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informational]));

        var result = ApplicationVersion.From(assembly);

        result.Version.Should().Be(version);
        result.Commit.Should().Be(commit);
        result.InformationalVersion.Should().Be(informational);
    }
}
