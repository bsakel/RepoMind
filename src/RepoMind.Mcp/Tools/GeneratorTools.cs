using System.ComponentModel;
using System.Text;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class GeneratorTools
{
    private readonly QueryService _query;

    public GeneratorTools(QueryService query) => _query = query;

    [McpServerTool(Name = "generate_agents_md"), Description(
        "Generate an AGENTS.md file for the scanned codebase. " +
        "Returns markdown content with product overview, project structure, dependency graph, and conventions. " +
        "Save the output to AGENTS.md in the target repo root.")]
    public string GenerateAgentsMd(
        [Description("Optional product/codebase name to use in the header")] string? productName = null)
        => _query.GenerateAgentsMd(productName);
}
