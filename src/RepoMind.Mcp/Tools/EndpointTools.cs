using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class EndpointTools
{
    private readonly QueryService _query;
    private readonly ILogger<EndpointTools> _logger;

    public EndpointTools(QueryService query, ILogger<EndpointTools> logger)
    {
        _query = query;
        _logger = logger;
    }

    [McpServerTool(Name = "search_endpoints"), Description(
        "Search for REST and GraphQL endpoints across all projects by route pattern. " +
        "Finds controllers with [HttpGet], [HttpPost] etc. and GraphQL [Query], [Mutation] methods. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string SearchEndpoints(
        [Description("Route pattern with optional wildcards, e.g. '/publish*', '*content*', or method name")] string routePattern,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "search_endpoints");
        _logger.LogDebug("Parameters: routePattern={RoutePattern}", routePattern);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.SearchEndpoints(routePattern);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format, limit: 100);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "search_methods"), Description(
        "Search for public methods across all types by name pattern. " +
        "Useful for finding method implementations across projects. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string SearchMethods(
        [Description("Method name pattern with optional wildcards, e.g. 'Create*', '*Async'")] string namePattern,
        [Description("Filter by return type (optional)")] string? returnType = null,
        [Description("Filter by project name (optional)")] string? projectName = null,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "search_methods");
        _logger.LogDebug("Parameters: namePattern={NamePattern}, returnType={ReturnType}, projectName={ProjectName}", namePattern, returnType, projectName);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.SearchMethods(namePattern, returnType, projectName);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format, limit: 100);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
