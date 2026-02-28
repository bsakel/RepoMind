using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class AnalysisTools
{
    private readonly QueryService _query;
    private readonly ILogger<AnalysisTools> _logger;

    public AnalysisTools(QueryService query, ILogger<AnalysisTools> logger)
    {
        _query = query;
        _logger = logger;
    }

    [McpServerTool(Name = "trace_flow"), Description(
        "Trace the flow of a type across projects: interface → implementors → who injects them → repeat. " +
        "Shows the full dependency chain to understand how a type is used throughout the codebase.")]
    public string TraceFlow(
        [Description("Type or interface name to trace, e.g. 'IPublishingService'")] string typeName,
        [Description("Maximum recursion depth (default 3)")] int maxDepth = 3)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "trace_flow");
        _logger.LogDebug("Parameters: typeName={TypeName}, maxDepth={MaxDepth}", typeName, maxDepth);
        try
        {
            return _query.TraceFlow(typeName, maxDepth);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "analyze_impact"), Description(
        "Analyze the blast radius of changing a type. Shows directly affected types (implementors, injectors, inheritors) " +
        "and transitively affected projects via NuGet dependencies.")]
    public string AnalyzeImpact(
        [Description("Type name to analyze impact for, e.g. 'ICoherentCache'")] string typeName)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "analyze_impact");
        _logger.LogDebug("Parameters: typeName={TypeName}", typeName);
        try
        {
            return _query.AnalyzeImpact(typeName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "detect_patterns"), Description(
        "Auto-detect architecture patterns in the codebase: Repository, Decorator, CQRS/Mediator, Factory, " +
        "Event Sourcing, Options, Service Layer, and God class warnings. " +
        "Optionally scoped to a specific project.")]
    public string DetectPatterns(
        [Description("Optional project name to scope detection to")] string? projectName = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "detect_patterns");
        _logger.LogDebug("Parameters: projectName={ProjectName}", projectName);
        try
        {
            return _query.DetectPatterns(projectName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
