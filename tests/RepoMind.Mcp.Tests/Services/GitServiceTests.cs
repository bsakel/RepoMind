using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace RepoMind.Mcp.Tests.Services;

public class GitServiceTests
{
    private readonly IProcessRunner _processRunner;
    private readonly GitService _sut;
    private readonly string _testRoot;

    public GitServiceTests()
    {
        _processRunner = Substitute.For<IProcessRunner>();
        _testRoot = Path.Combine(Path.GetTempPath(), "git-test-" + Guid.NewGuid().ToString("N")[..8]);
        var config = new RepoMindConfiguration
        {
            RootPath = _testRoot,
            DbPath = Path.Combine(_testRoot, "memory", "repomind.db"),
        };
        _sut = new GitService(config, _processRunner);
    }

    [Fact]
    public void GetAllRepoDirectories_FindsMatchingDirs()
    {
        // Create test directories
        Directory.CreateDirectory(_testRoot);
        var repo1 = Path.Combine(_testRoot, "acme.core");
        var repo2 = Path.Combine(_testRoot, "acme.caching");
        Directory.CreateDirectory(Path.Combine(repo1, ".git"));
        Directory.CreateDirectory(Path.Combine(repo2, ".git"));

        try
        {
            var result = _sut.GetAllRepoDirectories();
            result.Should().HaveCount(2);
            result.Should().Contain(d => d.Contains("acme.core"));
            result.Should().Contain(d => d.Contains("acme.caching"));
        }
        finally
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [Fact]
    public void GetAllRepoDirectories_SkipsNonGitDirs()
    {
        Directory.CreateDirectory(_testRoot);
        var withGit = Path.Combine(_testRoot, "acme.core");
        var withoutGit = Path.Combine(_testRoot, "acme.other");
        Directory.CreateDirectory(Path.Combine(withGit, ".git"));
        Directory.CreateDirectory(withoutGit); // no .git

        try
        {
            var result = _sut.GetAllRepoDirectories();
            result.Should().HaveCount(1);
            result.Should().Contain(d => d.Contains("acme.core"));
        }
        finally
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [Fact]
    public async Task GetBranchName_ReturnsMaster()
    {
        _processRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "master", ""));

        var result = await _sut.GetBranchName("/repo");
        result.Should().Be("master");
    }

    [Fact]
    public async Task GetBranchName_ReturnsFeatureBranch()
    {
        _processRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "feature/my-branch", ""));

        var result = await _sut.GetBranchName("/repo");
        result.Should().Be("feature/my-branch");
    }

    [Fact]
    public async Task GetRepoStatus_DetectsUncommittedChanges()
    {
        _processRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "master", ""));
        _processRunner.RunAsync("git", "status --porcelain", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, " M src/file.cs\n?? newfile.cs", ""));
        _processRunner.RunAsync("git", "rev-list --left-right --count HEAD...@{upstream}", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "0\t0", ""));

        var result = await _sut.GetRepoStatus("/repos/acme.core");
        result.HasUncommittedChanges.Should().BeTrue();
        result.Branch.Should().Be("master");
    }

    [Fact]
    public async Task GetRepoStatus_DetectsAheadBehind()
    {
        _processRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "master", ""));
        _processRunner.RunAsync("git", "status --porcelain", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));
        _processRunner.RunAsync("git", "rev-list --left-right --count HEAD...@{upstream}", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "2\t3", ""));

        var result = await _sut.GetRepoStatus("/repos/acme.core");
        result.Ahead.Should().Be(2);
        result.Behind.Should().Be(3);
        result.HasUncommittedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAndPull_NonMasterBranch_ReturnsSkipped()
    {
        _processRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "feature/test", ""));

        var result = await _sut.FetchAndPull("/repos/acme.core");
        result.Status.Should().Be(PullStatus.NonMasterBranch);
        result.Message.Should().Contain("feature/test");
    }

    [Fact]
    public async Task FetchAndPull_MasterBranch_PullsSuccessfully()
    {
        _processRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "master", ""));
        _processRunner.RunAsync("git", "fetch origin", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));
        _processRunner.RunAsync("git", "pull", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "Already up to date.", ""));

        var result = await _sut.FetchAndPull("/repos/acme.core");
        result.Status.Should().Be(PullStatus.Success);
        result.Message.Should().Contain("Already up to date");
    }
}
