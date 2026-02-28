using FluentAssertions;
using RepoMind.Scanner.Parsers;
using Xunit;

namespace RepoMind.Scanner.Tests.Parsers;

public class CsprojParserTests
{
    [Theory]
    [InlineData("src/MyApp/test/MyApp.UnitTests/MyApp.UnitTests.csproj")]
    [InlineData("src/MyApp/tests/MyApp.UnitTests/MyApp.UnitTests.csproj")]
    [InlineData("benchmark/MyApp.Benchmarks/MyApp.Benchmarks.csproj")]
    [InlineData("benchmarks/MyApp.Benchmarks/MyApp.Benchmarks.csproj")]
    public void IsTestProject_ReturnsTrueForTestPaths(string relativePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var csprojPath = Path.Combine(tempDir, "Dummy.csproj");
        File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup></PropertyGroup></Project>");

        try
        {
            CsprojParser.IsTestProject(relativePath, csprojPath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void IsTestProject_ReturnsFalseForBenchmarkServiceInSrc()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var csprojPath = Path.Combine(tempDir, "MyBenchmarkService.csproj");
        File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup></PropertyGroup></Project>");

        try
        {
            CsprojParser.IsTestProject("src/MyBenchmarkService/MyBenchmarkService.csproj", csprojPath)
                .Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void IsTestProject_ReturnsTrueWhenCsprojContainsXunitReference()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var csprojPath = Path.Combine(tempDir, "MyApp.Tests.csproj");
        File.WriteAllText(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""xunit"" Version=""2.9.3"" />
  </ItemGroup>
</Project>");

        try
        {
            CsprojParser.IsTestProject("src/MyApp/MyApp.Tests.csproj", csprojPath)
                .Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ParseProject_ParsesMinimalCsproj()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>MyApp.Core</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
</Project>";
            File.WriteAllText(Path.Combine(tempDir, "MyApp.Core.csproj"), csprojContent);

            var result = CsprojParser.ParseProject(tempDir, "TestProject");

            result.Should().HaveCount(1);
            var assembly = result[0];
            assembly.AssemblyName.Should().Be("MyApp.Core");
            assembly.TargetFramework.Should().Be("net10.0");
            assembly.PackageReferences.Should().ContainSingle(p => p.Name == "Newtonsoft.Json");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
