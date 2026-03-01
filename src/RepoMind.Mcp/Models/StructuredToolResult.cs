using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RepoMind.Mcp.Models;

/// <summary>
/// Wraps a tool result with structured metadata for programmatic consumption.
/// When format=json is requested, this object is serialized instead of raw markdown.
/// </summary>
public partial class StructuredToolResult
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("result_count")]
    public int ResultCount { get; init; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    [JsonPropertyName("query_ms")]
    public long QueryMs { get; init; }

    /// <summary>
    /// Creates a StructuredToolResult by parsing a markdown table to extract count/truncation info.
    /// </summary>
    /// <param name="markdown">The markdown content produced by QueryService.</param>
    /// <param name="queryMs">Elapsed query time in milliseconds.</param>
    /// <param name="limit">The SQL LIMIT used (if any) — if result_count == limit, truncated=true.</param>
    public static StructuredToolResult FromMarkdown(string markdown, long queryMs, int? limit = null)
    {
        // Count data rows in markdown tables (lines starting with | that aren't headers/separators)
        var count = 0;
        foreach (var line in markdown.AsSpan().EnumerateLines())
        {
            if (line.StartsWith("|") && !line.StartsWith("| ---") && !line.StartsWith("| -"))
            {
                count++;
            }
        }
        // Subtract header rows (one per table)
        var headerCount = 0;
        var inTable = false;
        foreach (var line in markdown.AsSpan().EnumerateLines())
        {
            if (line.StartsWith("| ---"))
            {
                headerCount++;
                inTable = true;
            }
            else if (inTable && !line.StartsWith("|"))
            {
                inTable = false;
            }
        }
        count = Math.Max(0, count - headerCount);

        var truncated = limit.HasValue && count >= limit.Value;

        return new StructuredToolResult
        {
            Content = markdown,
            ResultCount = count,
            Truncated = truncated,
            QueryMs = queryMs,
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
