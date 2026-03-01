using FluentAssertions;
using RepoMind.Mcp.Models;
using System.Text.Json;
using Xunit;

namespace RepoMind.Mcp.Tests;

public class StructuredToolResultTests
{
    [Fact]
    public void FromMarkdown_CountsTableRows()
    {
        var markdown = """
            | Type | Kind | Project |
            | --- | --- | --- |
            | FooService | class | acme.core |
            | BarService | class | acme.api |
            | BazService | class | acme.web |
            """;

        var result = StructuredToolResult.FromMarkdown(markdown, 5);

        result.ResultCount.Should().Be(3);
        result.QueryMs.Should().Be(5);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void FromMarkdown_DetectsTruncation_WhenCountEqualsLimit()
    {
        var markdown = """
            | Type | Kind |
            | --- | --- |
            | FooService | class |
            | BarService | class |
            """;

        var result = StructuredToolResult.FromMarkdown(markdown, 10, limit: 2);

        result.ResultCount.Should().Be(2);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public void FromMarkdown_NotTruncated_WhenCountBelowLimit()
    {
        var markdown = """
            | Type | Kind |
            | --- | --- |
            | FooService | class |
            """;

        var result = StructuredToolResult.FromMarkdown(markdown, 3, limit: 50);

        result.ResultCount.Should().Be(1);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void FromMarkdown_HandlesEmptyResult()
    {
        var markdown = "No types matching 'xyz'.";

        var result = StructuredToolResult.FromMarkdown(markdown, 1);

        result.ResultCount.Should().Be(0);
        result.Truncated.Should().BeFalse();
        result.Content.Should().Contain("No types matching");
    }

    [Fact]
    public void ToJson_ProducesValidJson()
    {
        var markdown = """
            | Type | Kind |
            | --- | --- |
            | FooService | class |
            """;

        var result = StructuredToolResult.FromMarkdown(markdown, 7, limit: 50);
        var json = result.ToJson();

        // Should be valid JSON
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("result_count").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("query_ms").GetInt64().Should().Be(7);
        doc.RootElement.GetProperty("content").GetString().Should().Contain("FooService");
    }

    [Fact]
    public void FromMarkdown_HandlesMultipleTables()
    {
        var markdown = """
            ## Table 1
            | Type | Kind |
            | --- | --- |
            | FooService | class |
            
            ## Table 2
            | Name | Value |
            | --- | --- |
            | key1 | val1 |
            | key2 | val2 |
            """;

        var result = StructuredToolResult.FromMarkdown(markdown, 2);

        // Should count rows from both tables
        result.ResultCount.Should().Be(3);
    }
}
