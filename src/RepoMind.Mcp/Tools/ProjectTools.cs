using System.ComponentModel;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class ProjectTools
{
    private readonly QueryService _query;

    public ProjectTools(QueryService query) => _query = query;

    [McpServerTool(Name = "list_projects"), Description(
        "List all scanned projects with summary info: " +
        "name, assembly count, type count.")]
    public string ListProjects() => _query.ListProjects();

    [McpServerTool(Name = "get_project_info"), Description(
        "Get detailed info for a specific project: assemblies, " +
        "namespaces, public types, NuGet dependencies, internal dependencies.")]
    public string GetProjectInfo(
        [Description("Project name, e.g. 'acme.caching' or partial match like 'caching'")]
        string projectName)
        => _query.GetProjectInfo(projectName);

    [McpServerTool(Name = "get_dependency_graph"), Description(
        "Show upstream dependencies (what a project consumes) and " +
        "downstream dependents (what consumes this project). " +
        "Helps understand blast radius of changes.")]
    public string GetDependencyGraph(
        [Description("Project name, e.g. 'acme.core'")]
        string projectName)
        => _query.GetDependencyGraph(projectName);
}
