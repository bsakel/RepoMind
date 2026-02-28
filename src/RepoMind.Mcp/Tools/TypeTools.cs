using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class TypeTools
{
    private readonly QueryService _query;
    private readonly ILogger<TypeTools> _logger;

    public TypeTools(QueryService query, ILogger<TypeTools> logger)
    {
        _query = query;
        _logger = logger;
    }

    [McpServerTool(Name = "search_types"), Description(
        "Search for types across all projects by name pattern, namespace, " +
        "kind (class/interface/enum/record), or project. " +
        "Supports wildcards: 'Publish*', '*Service', '*Cache*'.")]
    public string SearchTypes(
        [Description("Type name pattern with optional wildcards (*)")] string namePattern,
        [Description("Filter by namespace (optional)")] string? namespaceName = null,
        [Description("Filter by kind: class, interface, enum, record (optional)")] string? kind = null,
        [Description("Filter by project name (optional)")] string? projectName = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "search_types");
        _logger.LogDebug("Parameters: namePattern={NamePattern}, namespaceName={NamespaceName}, kind={Kind}, projectName={ProjectName}", namePattern, namespaceName, kind, projectName);
        try
        {
            return _query.SearchTypes(namePattern, namespaceName, kind, projectName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "find_implementors"), Description(
        "Find all classes that implement a given interface across all projects. " +
        "Critical for understanding DI registrations and extensibility points.")]
    public string FindImplementors(
        [Description("Interface name, e.g. 'IPublishingService' or 'ICoherentCache'")] string interfaceName)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "find_implementors");
        _logger.LogDebug("Parameters: interfaceName={InterfaceName}", interfaceName);
        try
        {
            return _query.FindImplementors(interfaceName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "find_type_details"), Description(
        "Get full details for a specific type: base class, implemented interfaces, " +
        "constructor-injected dependencies, source file path, project.")]
    public string FindTypeDetails(
        [Description("Exact type name, e.g. 'PublishingOrchestrator'")] string typeName)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "find_type_details");
        _logger.LogDebug("Parameters: typeName={TypeName}", typeName);
        try
        {
            return _query.GetTypeDetails(typeName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "get_type_summary"), Description(
        "Generate a natural-language summary of a type: what it is, its role, " +
        "interfaces, dependencies, complexity assessment, and coupling analysis. " +
        "Perfect for understanding unfamiliar types quickly.")]
    public string GetTypeSummary(
        [Description("Exact type name, e.g. 'QueryService'")] string typeName)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "get_type_summary");
        _logger.LogDebug("Parameters: typeName={TypeName}", typeName);
        try
        {
            return _query.GenerateTypeSummary(typeName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
