using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class ConfigTools
{
    private readonly QueryService _query;
    private readonly ILogger<ConfigTools> _logger;

    public ConfigTools(QueryService query, ILogger<ConfigTools> logger)
    {
        _query = query;
        _logger = logger;
    }

    [McpServerTool(Name = "search_config"), Description(
        "Search for configuration keys across all projects. " +
        "Finds keys from appsettings.json, environment variables, and IConfiguration usage in C# code.")]
    public string SearchConfig(
        [Description("Config key pattern with optional wildcards, e.g. 'ConnectionString', 'CosmosDb:*'")] string keyPattern,
        [Description("Filter by source: 'appsettings', 'env_var', or 'IConfiguration' (optional)")] string? source = null,
        [Description("Filter by project name (optional)")] string? projectName = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "search_config");
        _logger.LogDebug("Parameters: keyPattern={KeyPattern}, source={Source}, projectName={ProjectName}", keyPattern, source, projectName);
        try
        {
            return _query.SearchConfig(keyPattern, source, projectName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
