using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Mcp.Tests.TestFixtures;
using FluentAssertions;
using Xunit;

namespace RepoMind.Mcp.Tests.Tools;

public class QualityToolsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly QueryService _sut;

    public QualityToolsTests(TestDatabaseFixture fixture)
    {
        var config = new RepoMindConfiguration
        {
            RootPath = "/repos",
            DbPath = ":memory:",
        };
        _sut = new QueryService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryService>.Instance, () => fixture.Connection);
    }

    // --- Version Alignment tests ---

    [Fact]
    public void CheckVersionAlignment_DetectsMajorMismatch()
    {
        var result = _sut.CheckVersionAlignment();

        // HotChocolate 13.9.0 vs 14.0.0 = MAJOR
        result.Should().Contain("HotChocolate");
        result.Should().Contain("MAJOR");
    }

    [Fact]
    public void CheckVersionAlignment_DetectsMinorMismatch()
    {
        var result = _sut.CheckVersionAlignment();

        // Newtonsoft.Json 13.0.3 vs 13.0.1 = MINOR
        result.Should().Contain("Newtonsoft.Json");
        result.Should().Contain("MINOR");
    }

    [Fact]
    public void CheckVersionAlignment_ShowsSummary()
    {
        var result = _sut.CheckVersionAlignment();

        result.Should().Contain("Summary");
        result.Should().Contain("major mismatch");
        result.Should().Contain("minor mismatch");
    }

    // --- Test Coverage tests ---

    [Fact]
    public void FindUntestedTypes_FindsTypesWithoutTests()
    {
        var result = _sut.FindUntestedTypes();

        // CacheEvictionHandler has no CacheEvictionHandlerTests
        result.Should().Contain("CacheEvictionHandler");
        // ContentController has no ContentControllerTests
        result.Should().Contain("ContentController");
        // PublishingService has no PublishingServiceTests
        result.Should().Contain("PublishingService");
    }

    [Fact]
    public void FindUntestedTypes_ExcludesTestedTypes()
    {
        var result = _sut.FindUntestedTypes();

        // BaseEntity has BaseEntityTests — should NOT appear
        result.Should().NotContain("| BaseEntity |");
        // ContentItem has ContentItemTests — should NOT appear
        result.Should().NotContain("| ContentItem |");
        // CoherentCacheService has CoherentCacheServiceTests — should NOT appear
        result.Should().NotContain("| CoherentCacheService |");
    }

    [Fact]
    public void FindUntestedTypes_FilterByProject()
    {
        var result = _sut.FindUntestedTypes(projectName: "caching");

        result.Should().Contain("CacheEvictionHandler");
        result.Should().NotContain("ContentController");
    }

    [Fact]
    public void FindUntestedTypes_ExcludesConventionalTypes()
    {
        // Enums, interfaces, non-public types, *Options, etc. should be excluded
        var result = _sut.FindUntestedTypes();

        // CacheOptions is a record ending in Options — excluded
        result.Should().NotContain("| CacheOptions |");
        // Interfaces are excluded (only class/record)
        result.Should().NotContain("| ICoherentCache |");
        result.Should().NotContain("| IRepository |");
    }
}
