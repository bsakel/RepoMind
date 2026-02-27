using System.ComponentModel;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class AnalysisTools
{
    private readonly QueryService _query;

    public AnalysisTools(QueryService query) => _query = query;

    [McpServerTool(Name = "trace_flow"), Description(
        "Trace the flow of a type across projects: interface → implementors → who injects them → repeat. " +
        "Shows the full dependency chain to understand how a type is used throughout the codebase.")]
    public string TraceFlow(
        [Description("Type or interface name to trace, e.g. 'IPublishingService'")] string typeName,
        [Description("Maximum recursion depth (default 3)")] int maxDepth = 3)
        => _query.TraceFlow(typeName, maxDepth);

    [McpServerTool(Name = "analyze_impact"), Description(
        "Analyze the blast radius of changing a type. Shows directly affected types (implementors, injectors, inheritors) " +
        "and transitively affected projects via NuGet dependencies.")]
    public string AnalyzeImpact(
        [Description("Type name to analyze impact for, e.g. 'ICoherentCache'")] string typeName)
        => _query.AnalyzeImpact(typeName);
}
