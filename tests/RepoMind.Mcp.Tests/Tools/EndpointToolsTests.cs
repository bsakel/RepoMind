using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Mcp.Tests.TestFixtures;
using FluentAssertions;
using Xunit;

namespace RepoMind.Mcp.Tests.Tools;

public class EndpointToolsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly QueryService _sut;

    public EndpointToolsTests(TestDatabaseFixture fixture)
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/repos",
            DbPath = ":memory:",
        };
        _sut = new QueryService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryService>.Instance, () => fixture.Connection);
    }

    [Fact]
    public void SearchEndpoints_ByRoute_FindsMatches()
    {
        var result = _sut.SearchEndpoints("content");

        result.Should().Contain("GET");
        result.Should().Contain("api/content");
        result.Should().Contain("ContentController");
    }

    [Fact]
    public void SearchEndpoints_ByPublishRoute_FindsPublishEndpoint()
    {
        var result = _sut.SearchEndpoints("publish");

        result.Should().Contain("POST");
        result.Should().Contain("api/publish");
        result.Should().Contain("PublishingService");
    }

    [Fact]
    public void SearchEndpoints_NoMatch_ReturnsMessage()
    {
        var result = _sut.SearchEndpoints("nonexistent-route");

        result.Should().Contain("No endpoints matching");
    }

    [Fact]
    public void SearchMethods_ByName_FindsMatches()
    {
        var result = _sut.SearchMethods("Publish");

        result.Should().Contain("PublishAsync");
        result.Should().Contain("UnpublishAsync");
        result.Should().Contain("PublishingService");
    }

    [Fact]
    public void SearchMethods_ByReturnType_FiltersCorrectly()
    {
        var result = _sut.SearchMethods("*Async", returnType: "Task<bool>");

        result.Should().Contain("UnpublishAsync");
        result.Should().NotContain("PublishAsync");
    }

    [Fact]
    public void SearchMethods_ByProject_FiltersCorrectly()
    {
        var result = _sut.SearchMethods("*", projectName: "web.api");

        result.Should().Contain("GetContent");
        result.Should().Contain("PublishAsync");
        result.Should().NotContain("GetAsync"); // from caching project
    }

    [Fact]
    public void SearchMethods_NoMatch_ReturnsMessage()
    {
        var result = _sut.SearchMethods("NonExistentMethod");

        result.Should().Contain("No methods matching");
    }

    [Fact]
    public void SearchEndpoints_WildcardRoute_FindsAll()
    {
        var result = _sut.SearchEndpoints("*");

        result.Should().Contain("GET");
        result.Should().Contain("POST");
        result.Should().Contain("DELETE");
    }
}
