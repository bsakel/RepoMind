using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class QualityTools
{
    private readonly QueryService _query;
    private readonly ILogger<QualityTools> _logger;

    public QualityTools(QueryService query, ILogger<QualityTools> logger)
    {
        _query = query;
        _logger = logger;
    }

    [McpServerTool(Name = "check_version_alignment"), Description(
        "Check for NuGet package version mismatches across projects. " +
        "Reports packages used at different versions with MAJOR/MINOR severity classification. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string CheckVersionAlignment(
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "check_version_alignment");
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.CheckVersionAlignment();
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "get_memory_info"), Description(
        "Get information about the scanned memory database: size, last scan time, and row counts per table.")]
    public string GetMemoryInfo()
    {
        _logger.LogInformation("Tool {ToolName} invoked", "get_memory_info");
        try
        {
            return _query.GetMemoryInfo();
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "find_untested_types"), Description(
        "Find production types (classes/records) that don't have matching test classes. " +
        "Uses naming convention heuristics: FooService → FooServiceTests/FooServiceTest. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string FindUntestedTypes(
        [Description("Filter by project name (optional)")] string? projectName = null,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "find_untested_types");
        _logger.LogDebug("Parameters: projectName={ProjectName}", projectName);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.FindUntestedTypes(projectName);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
