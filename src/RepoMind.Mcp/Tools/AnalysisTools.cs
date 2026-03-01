using System.ComponentModel;
using System.Diagnostics;
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
        "Shows the full dependency chain to understand how a type is used throughout the codebase. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string TraceFlow(
        [Description("Type or interface name to trace, e.g. 'IPublishingService'")] string typeName,
        [Description("Maximum recursion depth (default 3)")] int maxDepth = 3,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "trace_flow");
        _logger.LogDebug("Parameters: typeName={TypeName}, maxDepth={MaxDepth}", typeName, maxDepth);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.TraceFlow(typeName, maxDepth);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "analyze_impact"), Description(
        "Analyze the blast radius of changing a type. Shows directly affected types (implementors, injectors, inheritors) " +
        "and transitively affected projects via NuGet dependencies. " +
        "Includes a human-readable impact narrative and Mermaid diagram. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string AnalyzeImpact(
        [Description("Type name to analyze impact for, e.g. 'ICoherentCache'")] string typeName,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "analyze_impact");
        _logger.LogDebug("Parameters: typeName={TypeName}", typeName);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.AnalyzeImpact(typeName);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "detect_patterns"), Description(
        "Auto-detect architecture patterns in the codebase: Repository, Decorator, CQRS/Mediator, Factory, " +
        "Event Sourcing, Options, Service Layer, and God class warnings. " +
        "Optionally scoped to a specific project. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string DetectPatterns(
        [Description("Optional project name to scope detection to")] string? projectName = null,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "detect_patterns");
        _logger.LogDebug("Parameters: projectName={ProjectName}", projectName);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.DetectPatterns(projectName);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
