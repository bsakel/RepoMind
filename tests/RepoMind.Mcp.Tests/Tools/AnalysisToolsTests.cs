using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Mcp.Tests.TestFixtures;
using FluentAssertions;
using Xunit;

namespace RepoMind.Mcp.Tests.Tools;

public class AnalysisToolsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly QueryService _sut;

    public AnalysisToolsTests(TestDatabaseFixture fixture)
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/repos",
            DbPath = ":memory:",
        };
        _sut = new QueryService(config) { TestConnection = fixture.Connection };
    }

    // --- TraceFlow tests ---

    [Fact]
    public void TraceFlow_Interface_FindsImplementorsAndInjectors()
    {
        var result = _sut.TraceFlow("ICoherentCache");

        result.Should().Contain("ICoherentCache");
        result.Should().Contain("CoherentCacheService"); // implementor
    }

    [Fact]
    public void TraceFlow_FindsInjectionChain()
    {
        var result = _sut.TraceFlow("ICoherentCache");

        // ICoherentCache is injected into CacheEvictionHandler and ContentController
        result.Should().Contain("CacheEvictionHandler");
        result.Should().Contain("ContentController");
    }

    [Fact]
    public void TraceFlow_UnknownType_ReportsNoConnections()
    {
        var result = _sut.TraceFlow("NonExistentType");

        result.Should().Contain("No flow connections found");
    }

    [Fact]
    public void TraceFlow_RespectsMaxDepth()
    {
        // With depth 0, should only show direct connections
        var result = _sut.TraceFlow("ICoherentCache", maxDepth: 0);

        // Should still contain the header
        result.Should().Contain("ICoherentCache");
    }

    // --- AnalyzeImpact tests ---

    [Fact]
    public void AnalyzeImpact_Interface_ShowsImplementors()
    {
        var result = _sut.AnalyzeImpact("ICoherentCache");

        result.Should().Contain("Defined In");
        result.Should().Contain("acme.caching");
        result.Should().Contain("CoherentCacheService");
        result.Should().Contain("implements");
    }

    [Fact]
    public void AnalyzeImpact_Interface_ShowsInjectors()
    {
        var result = _sut.AnalyzeImpact("ICoherentCache");

        result.Should().Contain("CacheEvictionHandler");
        result.Should().Contain("injects");
    }

    [Fact]
    public void AnalyzeImpact_ShowsSummary()
    {
        var result = _sut.AnalyzeImpact("ICoherentCache");

        result.Should().Contain("Summary");
        result.Should().Contain("Direct references");
        result.Should().Contain("blast radius");
    }

    [Fact]
    public void AnalyzeImpact_UnknownType_ReportsNotFound()
    {
        var result = _sut.AnalyzeImpact("NonExistentType");

        result.Should().Contain("not found");
    }
}
