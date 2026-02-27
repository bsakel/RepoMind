using System.ComponentModel;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class DependencyTools
{
    private readonly QueryService _query;

    public DependencyTools(QueryService query) => _query = query;

    [McpServerTool(Name = "search_injections"), Description(
        "Find all types that inject a given dependency via constructor injection. " +
        "Shows which projects and classes rely on a service.")]
    public string SearchInjections(
        [Description("Dependency type name, e.g. 'ICoherentCache', 'IMapper', 'ILogger'")] string dependencyName)
        => _query.SearchInjections(dependencyName);

    [McpServerTool(Name = "get_package_versions"), Description(
        "Show which version of a NuGet package each project uses. " +
        "Useful for detecting version mismatches across the product.")]
    public string GetPackageVersions(
        [Description("NuGet package name, e.g. 'Acme.Core', 'HotChocolate'")] string packageName)
        => _query.GetPackageVersions(packageName);
}
