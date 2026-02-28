using System.ComponentModel;
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
        "Shows which projects and classes rely on a service.")]
    public string SearchInjections(
        [Description("Dependency type name, e.g. 'ICoherentCache', 'IMapper', 'ILogger'")] string dependencyName)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "search_injections");
        _logger.LogDebug("Parameters: dependencyName={DependencyName}", dependencyName);
        try
        {
            return _query.SearchInjections(dependencyName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "get_package_versions"), Description(
        "Show which version of a NuGet package each project uses. " +
        "Useful for detecting version mismatches across the product.")]
    public string GetPackageVersions(
        [Description("NuGet package name, e.g. 'Acme.Core', 'HotChocolate'")] string packageName)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "get_package_versions");
        _logger.LogDebug("Parameters: packageName={PackageName}", packageName);
        try
        {
            return _query.GetPackageVersions(packageName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
