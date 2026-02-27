using System.ComponentModel;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class ConfigTools
{
    private readonly QueryService _query;

    public ConfigTools(QueryService query) => _query = query;

    [McpServerTool(Name = "search_config"), Description(
        "Search for configuration keys across all projects. " +
        "Finds keys from appsettings.json, environment variables, and IConfiguration usage in C# code.")]
    public string SearchConfig(
        [Description("Config key pattern with optional wildcards, e.g. 'ConnectionString', 'CosmosDb:*'")] string keyPattern,
        [Description("Filter by source: 'appsettings', 'env_var', or 'IConfiguration' (optional)")] string? source = null,
        [Description("Filter by project name (optional)")] string? projectName = null)
        => _query.SearchConfig(keyPattern, source, projectName);
}
