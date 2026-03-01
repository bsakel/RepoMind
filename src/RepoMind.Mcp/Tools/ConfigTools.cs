using System.ComponentModel;
using System.Diagnostics;
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
        "Finds keys from appsettings.json, environment variables, and IConfiguration usage in C# code. " +
        "Set format='json' for structured output with result count and query timing.")]
    public string SearchConfig(
        [Description("Config key pattern with optional wildcards, e.g. 'ConnectionString', 'CosmosDb:*'")] string keyPattern,
        [Description("Filter by source: 'appsettings', 'env_var', or 'IConfiguration' (optional)")] string? source = null,
        [Description("Filter by project name (optional)")] string? projectName = null,
        [Description("Output format: 'markdown' (default) or 'json' for structured results")] string? format = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "search_config");
        _logger.LogDebug("Parameters: keyPattern={KeyPattern}, source={Source}, projectName={ProjectName}", keyPattern, source, projectName);
        try
        {
            var sw = Stopwatch.StartNew();
            var result = _query.SearchConfig(keyPattern, source, projectName);
            sw.Stop();
            return ToolResultFormatter.Format(result, sw.ElapsedMilliseconds, format, limit: 100);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
