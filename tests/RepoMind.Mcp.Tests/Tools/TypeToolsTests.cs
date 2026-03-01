using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Mcp.Tests.TestFixtures;
using RepoMind.Mcp.Tools;
using FluentAssertions;
using Xunit;

namespace RepoMind.Mcp.Tests.Tools;

public class TypeToolsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TypeTools _sut;

    public TypeToolsTests(TestDatabaseFixture fixture)
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/repos",
            DbPath = ":memory:",
        };
        var queryService = new QueryService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryService>.Instance, () => fixture.Connection);
        _sut = new TypeTools(queryService, Microsoft.Extensions.Logging.Abstractions.NullLogger<TypeTools>.Instance);
    }

    [Fact]
    public void SearchTypes_FormatsResults()
    {
        var result = _sut.SearchTypes("*Service*");

        result.Should().Contain("| Type |");
        result.Should().Contain("CoherentCacheService");
        result.Should().Contain("PublishingService");
        result.Should().Contain("acme.caching");
    }

    [Fact]
    public void FindImplementors_FormatsResults()
    {
        var result = _sut.FindImplementors("ICoherentCache");

        result.Should().Contain("| Implementing Type |");
        result.Should().Contain("CoherentCacheService");
        result.Should().Contain("acme.caching");
    }

    [Fact]
    public void FindTypeDetails_FormatsAllInfo()
    {
        var result = _sut.FindTypeDetails("CoherentCacheService");

        result.Should().Contain("CoherentCacheService");
        result.Should().Contain("Implements:");
        result.Should().Contain("ICoherentCache");
        result.Should().Contain("Injected Dependencies:");
        result.Should().Contain("ILogger<CoherentCacheService>");
    }

    [Fact]
    public void SearchTypes_NoResults_ReturnsMessage()
    {
        var result = _sut.SearchTypes("ZZZNonExistent");

        result.Should().Contain("No types matching");
    }

    [Fact]
    public void SearchTypes_JsonFormat_ReturnsStructuredResult()
    {
        var result = _sut.SearchTypes("*Service*", format: "json");

        result.Should().Contain("\"result_count\"");
        result.Should().Contain("\"query_ms\"");
        result.Should().Contain("\"truncated\"");
        result.Should().Contain("\"content\"");
        result.Should().Contain("CoherentCacheService");
    }

    [Fact]
    public void FindImplementors_JsonFormat_ReturnsStructuredResult()
    {
        var result = _sut.FindImplementors("ICoherentCache", format: "json");

        result.Should().Contain("\"result_count\"");
        result.Should().Contain("CoherentCacheService");
    }

    [Fact]
    public void SearchTypes_NullFormat_ReturnsMarkdown()
    {
        var result = _sut.SearchTypes("*Service*", format: null);

        result.Should().Contain("| Type |");
        result.Should().NotContain("\"result_count\"");
    }
}
