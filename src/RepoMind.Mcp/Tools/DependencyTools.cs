using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class DependencyTools
{
    private readonly QueryService _query;
    private readonly ILogger<DependencyTools> _logger;

    public DependencyTools(QueryService query, ILogger<DependencyTools> logger)
    {
        _query = query;
        _logger = logger;
    }

    [McpServerTool(Name = "search_injections"), Description(
        "Find all types that inject a given dependency via constructor injection. " +
        "Shows which projects and classes rely on a service. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string SearchInjections(
        [Description("Dependency type name, e.g. 'ICoherentCache', 'IMapper', 'ILogger'")] string dependencyName,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "search_injections");
        _logger.LogDebug("Parameters: dependencyName={DependencyName}", dependencyName);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.SearchInjections(dependencyName);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format, limit: 100);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "get_package_versions"), Description(
        "Show which version of a NuGet package each project uses. " +
        "Useful for detecting version mismatches across the product. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string GetPackageVersions(
        [Description("NuGet package name, e.g. 'Acme.Core', 'HotChocolate'")] string packageName,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "get_package_versions");
        _logger.LogDebug("Parameters: packageName={PackageName}", packageName);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.GetPackageVersions(packageName);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
