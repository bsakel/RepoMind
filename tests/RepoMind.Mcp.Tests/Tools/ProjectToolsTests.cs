using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Mcp.Tests.TestFixtures;
using RepoMind.Mcp.Tools;
using FluentAssertions;
using Xunit;

namespace RepoMind.Mcp.Tests.Tools;

public class ProjectToolsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly ProjectTools _sut;

    public ProjectToolsTests(TestDatabaseFixture fixture)
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/repos",
            DbPath = ":memory:",
        };
        var queryService = new QueryService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryService>.Instance, () => fixture.Connection);
        _sut = new ProjectTools(queryService, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectTools>.Instance);
    }

    [Fact]
    public void ListProjects_ReturnsFormattedTable()
    {
        var result = _sut.ListProjects();

        result.Should().Contain("| Project |");
        result.Should().Contain("acme.core");
        result.Should().Contain("acme.caching");
        result.Should().Contain("acme.web.api");
    }

    [Fact]
    public void GetProjectInfo_IncludesAllSections()
    {
        var result = _sut.GetProjectInfo("acme.caching");

        result.Should().Contain("# acme.caching");
        result.Should().Contain("## Assemblies");
        result.Should().Contain("## Internal Dependencies");
        result.Should().Contain("## Namespaces");
        result.Should().Contain("## Key Public Types");
    }

    [Fact]
    public void GetDependencyGraph_ShowsBothDirections()
    {
        var result = _sut.GetDependencyGraph("acme.caching");

        result.Should().Contain("Depends On");
        result.Should().Contain("Depended On By");
    }

    [Fact]
    public void GetProjectInfo_PartialName_Works()
    {
        var result = _sut.GetProjectInfo("web.api");

        result.Should().Contain("# acme.web.api");
    }

    [Fact]
    public void GetProjectInfo_NotFound_ReturnsMessage()
    {
        var result = _sut.GetProjectInfo("nonexistent");

        result.Should().Contain("not found");
    }
}
