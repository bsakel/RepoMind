using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Mcp.Tests.TestFixtures;
using RepoMind.Mcp.Tools;
using FluentAssertions;
using Xunit;

namespace RepoMind.Mcp.Tests.Tools;

public class DependencyToolsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly DependencyTools _sut;

    public DependencyToolsTests(TestDatabaseFixture fixture)
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/repos",
            DbPath = ":memory:",
        };
        var queryService = new QueryService(config) { TestConnection = fixture.Connection };
        _sut = new DependencyTools(queryService);
    }

    [Fact]
    public void SearchInjections_FormatsResults()
    {
        var result = _sut.SearchInjections("ICoherentCache");

        result.Should().Contain("| Type |");
        result.Should().Contain("CacheEvictionHandler");
        result.Should().Contain("ContentController");
        result.Should().Contain("PublishingService");
    }

    [Fact]
    public void GetPackageVersions_FormatsVersionTable()
    {
        var result = _sut.GetPackageVersions("Newtonsoft.Json");

        result.Should().Contain("| Package |");
        result.Should().Contain("Newtonsoft.Json");
        result.Should().Contain("acme.core");
    }

    [Fact]
    public void GetPackageVersions_HighlightsMismatches()
    {
        var result = _sut.GetPackageVersions("Newtonsoft.Json");

        // 13.0.3 in common/caching, 13.0.1 in cm.api
        result.Should().Contain("mismatch");
    }
}
