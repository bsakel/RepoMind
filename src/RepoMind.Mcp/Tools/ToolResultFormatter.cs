using RepoMind.Mcp.Models;

namespace RepoMind.Mcp.Tools;

/// <summary>
/// Shared helper for formatting tool results as markdown or structured JSON.
/// </summary>
internal static class ToolResultFormatter
{
    /// <summary>
    /// Returns the result as-is (markdown) or wrapped in structured JSON with metadata.
    /// </summary>
    /// <param name="markdown">The markdown content from QueryService.</param>
    /// <param name="queryMs">Elapsed query time in milliseconds.</param>
    /// <param name="format">Output format: null/"markdown" for raw markdown, "json" for structured.</param>
    /// <param name="limit">The SQL LIMIT used, to detect truncation.</param>
    public static string Format(string markdown, long queryMs, string? format, int? limit = null)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return StructuredToolResult.FromMarkdown(markdown, queryMs, limit).ToJson();
        }
        return markdown;
    }
}
