using FluentAssertions;
using RepoMind.Scanner;
using Xunit;

namespace RepoMind.Scanner.Tests;

public class ScannerEngineTests
{
    [Fact]
    public void Run_WithEmptyDirectory_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputDir = Path.Combine(tempDir, "memory");
        Directory.CreateDirectory(tempDir);

        try
        {
            var engine = new ScannerEngine();
            var result = engine.Run(new ScanOptions(tempDir, outputDir));

            result.Success.Should().BeTrue();
            result.ProjectCount.Should().Be(0);
            result.TypeCount.Should().Be(0);
            result.FailedProjects.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Run_ContinuesAfterProjectFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputDir = Path.Combine(tempDir, "memory");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a valid repo structure
            var goodRepo = Path.Combine(tempDir, "good-repo");
            Directory.CreateDirectory(Path.Combine(goodRepo, ".git"));
            File.WriteAllText(Path.Combine(goodRepo, "App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            // Create a repo with a malformed csproj that will cause parse errors
            var badRepo = Path.Combine(tempDir, "bad-repo");
            Directory.CreateDirectory(Path.Combine(badRepo, ".git"));
            File.WriteAllText(Path.Combine(badRepo, "Bad.csproj"), "<<<NOT VALID XML>>>");

            var engine = new ScannerEngine();
            var result = engine.Run(new ScanOptions(tempDir, outputDir, SqliteOnly: true));

            // Should still succeed overall (partial success)
            result.Success.Should().BeTrue();
            // At least the good repo should be scanned
            result.ProjectCount.Should().BeGreaterOrEqualTo(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Run_SkipsNonGitDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputDir = Path.Combine(tempDir, "memory");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a non-git directory
            Directory.CreateDirectory(Path.Combine(tempDir, "not-a-repo"));

            // Create a git directory
            var gitRepo = Path.Combine(tempDir, "my-repo");
            Directory.CreateDirectory(Path.Combine(gitRepo, ".git"));

            var engine = new ScannerEngine();
            var result = engine.Run(new ScanOptions(tempDir, outputDir, SqliteOnly: true));

            result.Success.Should().BeTrue();
            result.ProjectCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Run_SkipsHiddenDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputDir = Path.Combine(tempDir, "memory");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a hidden directory with .git (should be skipped)
            Directory.CreateDirectory(Path.Combine(tempDir, ".hidden-repo", ".git"));

            // Create a visible directory with .git
            Directory.CreateDirectory(Path.Combine(tempDir, "visible-repo", ".git"));

            var engine = new ScannerEngine();
            var result = engine.Run(new ScanOptions(tempDir, outputDir, SqliteOnly: true));

            result.Success.Should().BeTrue();
            result.ProjectCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
