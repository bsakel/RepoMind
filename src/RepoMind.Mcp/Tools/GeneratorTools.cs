using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class GeneratorTools
{
    private readonly QueryService _query;
    private readonly ILogger<GeneratorTools> _logger;

    public GeneratorTools(QueryService query, ILogger<GeneratorTools> logger)
    {
        _query = query;
        _logger = logger;
    }

    [McpServerTool(Name = "generate_agents_md"), Description(
        "Generate an AGENTS.md file for the scanned codebase. " +
        "Returns markdown content with product overview, project structure, dependency graph, and conventions. " +
        "Save the output to AGENTS.md in the target repo root.")]
    public string GenerateAgentsMd(
        [Description("Optional product/codebase name to use in the header")] string? productName = null)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "generate_agents_md");
        _logger.LogDebug("Parameters: productName={ProductName}", productName);
        try
        {
            return _query.GenerateAgentsMd(productName);
        }
        catch (DatabaseNotFoundException ex)
        {
            return ex.Message;
        }
    }
}
