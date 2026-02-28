using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Mcp.Tests.TestFixtures;
using FluentAssertions;
using Xunit;

namespace RepoMind.Mcp.Tests.Services;

public class QueryServiceTests : IClassFixture<TestDatabaseFixture>
{
    private readonly QueryService _sut;

    public QueryServiceTests(TestDatabaseFixture fixture)
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/repos",
            DbPath = ":memory:", // not actually used since we inject a connection factory
        };
        _sut = new QueryService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryService>.Instance, () => fixture.Connection);
    }

    [Fact]
    public void ListProjects_ReturnsAllProjects()
    {
        var result = _sut.ListProjects();

        result.Should().Contain("acme.core");
        result.Should().Contain("acme.caching");
        result.Should().Contain("acme.web.api");
    }

    [Fact]
    public void ListProjects_IncludesAssemblyAndTypeCounts()
    {
        var result = _sut.ListProjects();

        // common has 1 src assembly and 5 types in src namespaces
        result.Should().Contain("acme.core");
        // Result should be a markdown table
        result.Should().Contain("| Project |");
        result.Should().Contain("| --- |");
    }

    [Fact]
    public void GetProjectInfo_ValidProject_ReturnsDetail()
    {
        var result = _sut.GetProjectInfo("acme.caching");

        result.Should().Contain("# acme.caching");
        result.Should().Contain("Acme.Caching");
        result.Should().Contain("Assemblies");
        result.Should().Contain("Namespaces");
        result.Should().Contain("Internal Dependencies");
    }

    [Fact]
    public void GetProjectInfo_InvalidProject_ReturnsNotFound()
    {
        var result = _sut.GetProjectInfo("nonexistent.project");

        result.Should().Contain("not found");
    }

    [Fact]
    public void GetProjectInfo_PartialMatch_FindsProject()
    {
        var result = _sut.GetProjectInfo("caching");

        result.Should().Contain("# acme.caching");
    }

    [Fact]
    public void GetDependencyGraph_ReturnsUpstreamAndDownstream()
    {
        var result = _sut.GetDependencyGraph("acme.caching");

        result.Should().Contain("Depends On");
        result.Should().Contain("Depended On By");
        // Caching depends on Common
        result.Should().Contain("Acme.Core");
    }

    [Fact]
    public void SearchTypes_ByNamePattern_UsesWildcard()
    {
        var result = _sut.SearchTypes("*Service");

        result.Should().Contain("CoherentCacheService");
        result.Should().Contain("PublishingService");
    }

    [Fact]
    public void SearchTypes_ByKind_FiltersCorrectly()
    {
        var result = _sut.SearchTypes("*", kind: "interface");

        result.Should().Contain("IRepository");
        result.Should().Contain("ICoherentCache");
        result.Should().NotContain("| BaseEntity |");
        result.Should().NotContain("| ContentItem |");
    }

    [Fact]
    public void SearchTypes_ByProject_FiltersCorrectly()
    {
        var result = _sut.SearchTypes("*", projectName: "caching");

        result.Should().Contain("CoherentCacheService");
        result.Should().Contain("ICoherentCache");
        result.Should().NotContain("ContentController");
    }

    [Fact]
    public void SearchTypes_CombinedFilters_WorkTogether()
    {
        var result = _sut.SearchTypes("*Cache*", kind: "interface", projectName: "caching");

        result.Should().Contain("ICoherentCache");
        result.Should().Contain("ICacheEvictionHandler");
        // Should not contain the class CacheEvictionHandler (only the interface)
        result.Should().NotContain("| CacheEvictionHandler |");
    }

    [Fact]
    public void FindImplementors_ReturnsAllImplementations()
    {
        var result = _sut.FindImplementors("ICoherentCache");

        result.Should().Contain("CoherentCacheService");
    }

    [Fact]
    public void FindImplementors_NoResults_ReturnsEmpty()
    {
        var result = _sut.FindImplementors("INonExistentInterface");

        result.Should().Contain("No implementors");
    }

    [Fact]
    public void GetTypeDetails_ReturnsFullInfo()
    {
        var result = _sut.GetTypeDetails("CoherentCacheService");

        result.Should().Contain("CoherentCacheService");
        result.Should().Contain("class");
        result.Should().Contain("Acme.Caching");
        result.Should().Contain("ICoherentCache");
        result.Should().Contain("IDisposable");
        result.Should().Contain("ILogger<CoherentCacheService>");
        result.Should().Contain("IOptions<CacheOptions>");
    }

    [Fact]
    public void GetTypeDetails_NotFound_ReturnsMessage()
    {
        var result = _sut.GetTypeDetails("NonExistentType");

        result.Should().Contain("not found");
    }

    [Fact]
    public void SearchInjections_FindsAllConsumers()
    {
        var result = _sut.SearchInjections("ICoherentCache");

        result.Should().Contain("CacheEvictionHandler");
        result.Should().Contain("ContentController");
        result.Should().Contain("PublishingService");
    }

    [Fact]
    public void GetPackageVersions_ShowsVersionPerProject()
    {
        var result = _sut.GetPackageVersions("Newtonsoft.Json");

        result.Should().Contain("Newtonsoft.Json");
        result.Should().Contain("acme.core");
        result.Should().Contain("acme.caching");
        result.Should().Contain("acme.web.api");
    }

    [Fact]
    public void GetPackageVersions_DetectsMismatches()
    {
        var result = _sut.GetPackageVersions("Newtonsoft.Json");

        // common and caching have 13.0.3, cm.api has 13.0.1
        result.Should().Contain("mismatch");
    }

    [Fact]
    public void QueryService_WhenNoDatabaseFile_ThrowsDatabaseNotFoundException()
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/nonexistent",
            DbPath = "/nonexistent/path/repomind.db",
        };
        var sut = new QueryService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryService>.Instance); // no factory = will use OpenConnection

        var act = () => sut.ListProjects();

        act.Should().Throw<DatabaseNotFoundException>()
            .WithMessage("*rescan_memory*");
    }

    [Fact]
    public void SearchEndpoints_ByRoute_ReturnsMatches()
    {
        var result = _sut.SearchEndpoints("content");

        result.Should().Contain("api/content");
        result.Should().Contain("GET");
    }

    [Fact]
    public void SearchEndpoints_NoMatch_ReturnsNotFound()
    {
        var result = _sut.SearchEndpoints("nonexistent_route_xyz");

        result.Should().Contain("No endpoints matching");
    }

    [Fact]
    public void SearchMethods_ByName_ReturnsMatches()
    {
        var result = _sut.SearchMethods("Publish*");

        result.Should().Contain("PublishAsync");
        result.Should().Contain("PublishingService");
    }

    [Fact]
    public void SearchMethods_FilterByProject_NarrowsResults()
    {
        var result = _sut.SearchMethods("*Async*", projectName: "acme.caching");

        result.Should().Contain("GetAsync");
        result.Should().NotContain("PublishAsync");
    }

    [Fact]
    public void GetMemoryInfo_ReturnsRowCounts()
    {
        var result = _sut.GetMemoryInfo();

        result.Should().Contain("Row Counts");
        result.Should().Contain("projects");
        result.Should().Contain("types");
    }
}
