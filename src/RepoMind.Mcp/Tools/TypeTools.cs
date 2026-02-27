using System.ComponentModel;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class TypeTools
{
    private readonly QueryService _query;

    public TypeTools(QueryService query) => _query = query;

    [McpServerTool(Name = "search_types"), Description(
        "Search for types across all projects by name pattern, namespace, " +
        "kind (class/interface/enum/record), or project. " +
        "Supports wildcards: 'Publish*', '*Service', '*Cache*'.")]
    public string SearchTypes(
        [Description("Type name pattern with optional wildcards (*)")] string namePattern,
        [Description("Filter by namespace (optional)")] string? namespaceName = null,
        [Description("Filter by kind: class, interface, enum, record (optional)")] string? kind = null,
        [Description("Filter by project name (optional)")] string? projectName = null)
        => _query.SearchTypes(namePattern, namespaceName, kind, projectName);

    [McpServerTool(Name = "find_implementors"), Description(
        "Find all classes that implement a given interface across all projects. " +
        "Critical for understanding DI registrations and extensibility points.")]
    public string FindImplementors(
        [Description("Interface name, e.g. 'IPublishingService' or 'ICoherentCache'")] string interfaceName)
        => _query.FindImplementors(interfaceName);

    [McpServerTool(Name = "find_type_details"), Description(
        "Get full details for a specific type: base class, implemented interfaces, " +
        "constructor-injected dependencies, source file path, project.")]
    public string FindTypeDetails(
        [Description("Exact type name, e.g. 'PublishingOrchestrator'")] string typeName)
        => _query.GetTypeDetails(typeName);
}
