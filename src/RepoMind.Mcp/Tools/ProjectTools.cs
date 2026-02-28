using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class ProjectTools
{
    private readonly QueryService _query;
    private readonly ILogger<ProjectTools> _logger;

    public ProjectTools(QueryService query, ILogger<ProjectTools> logger)
    {
        _query = query;
        _logger = logger;
    }

    [McpServerTool(Name = "list_projects"), Description(
        "List all scanned projects with summary info: " +
        "name, assembly count, type count.")]
    public string ListProjects()
    {
        _logger.LogInformation("Tool {ToolName} invoked", "list_projects");
        try
        {
            return _query.ListProjects();
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "get_project_info"), Description(
        "Get detailed info for a specific project: assemblies, " +
        "namespaces, public types, NuGet dependencies, internal dependencies.")]
    public string GetProjectInfo(
        [Description("Project name, e.g. 'acme.caching' or partial match like 'caching'")]
        string projectName)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "get_project_info");
        _logger.LogDebug("Parameters: projectName={ProjectName}", projectName);
        try
        {
            return _query.GetProjectInfo(projectName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }

    [McpServerTool(Name = "get_dependency_graph"), Description(
        "Show upstream dependencies (what a project consumes) and " +
        "downstream dependents (what consumes this project). " +
        "Helps understand blast radius of changes.")]
    public string GetDependencyGraph(
        [Description("Project name, e.g. 'acme.core'")]
        string projectName)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "get_dependency_graph");
        _logger.LogDebug("Parameters: projectName={ProjectName}", projectName);
        try
        {
            return _query.GetDependencyGraph(projectName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
