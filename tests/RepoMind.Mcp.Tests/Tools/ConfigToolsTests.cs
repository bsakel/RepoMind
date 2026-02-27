using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Mcp.Tests.TestFixtures;
using FluentAssertions;
using Xunit;

namespace RepoMind.Mcp.Tests.Tools;

public class ConfigToolsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly QueryService _sut;

    public ConfigToolsTests(TestDatabaseFixture fixture)
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/repos",
            DbPath = ":memory:",
        };
        _sut = new QueryService(config) { TestConnection = fixture.Connection };
    }

    [Fact]
    public void SearchConfig_ByKeyPattern_FindsMatches()
    {
        var result = _sut.SearchConfig("CosmosDb");

        result.Should().Contain("CosmosDb:ConnectionString");
        result.Should().Contain("CosmosDb:DatabaseName");
        result.Should().Contain("acme.web.api");
    }

    [Fact]
    public void SearchConfig_FilterBySource_ReturnsOnlyMatchingSource()
    {
        var result = _sut.SearchConfig("*", source: "env_var");

        result.Should().Contain("ASPNETCORE_ENVIRONMENT");
        result.Should().Contain("LOG_LEVEL");
        result.Should().NotContain("CosmosDb:ConnectionString");
    }

    [Fact]
    public void SearchConfig_FilterByProject_ReturnsOnlyMatchingProject()
    {
        var result = _sut.SearchConfig("*", projectName: "caching");

        result.Should().Contain("Caching:DefaultTtlSeconds");
        result.Should().Contain("Caching:RedisConnectionString");
        result.Should().NotContain("CosmosDb");
    }

    [Fact]
    public void SearchConfig_ShowsDefaultValues()
    {
        var result = _sut.SearchConfig("DefaultTtl");

        result.Should().Contain("300");
    }

    [Fact]
    public void SearchConfig_NoMatch_ReturnsMessage()
    {
        var result = _sut.SearchConfig("NonExistentConfigKey");

        result.Should().Contain("No config keys matching");
    }

    [Fact]
    public void SearchConfig_AllSources_FindsAll()
    {
        var result = _sut.SearchConfig("*", projectName: "web.api");

        result.Should().Contain("appsettings");
        result.Should().Contain("env_var");
        result.Should().Contain("IConfiguration");
    }
}
