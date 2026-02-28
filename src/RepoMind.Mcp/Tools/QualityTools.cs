using System.ComponentModel;
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
        "Reports packages used at different versions with MAJOR/MINOR severity classification.")]
    public string CheckVersionAlignment()
    {
        _logger.LogInformation("Tool {ToolName} invoked", "check_version_alignment");
        try
        {
            return _query.CheckVersionAlignment();
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
        "Uses naming convention heuristics: FooService → FooServiceTests/FooServiceTest.")]
    public string FindUntestedTypes(
        [Description("Filter by project name (optional)")] string? projectName = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "find_untested_types");
        _logger.LogDebug("Parameters: projectName={ProjectName}", projectName);
        try
        {
            return _query.FindUntestedTypes(projectName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
