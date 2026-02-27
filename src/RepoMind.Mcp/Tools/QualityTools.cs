using System.ComponentModel;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class QualityTools
{
    private readonly QueryService _query;

    public QualityTools(QueryService query) => _query = query;

    [McpServerTool(Name = "check_version_alignment"), Description(
        "Check for NuGet package version mismatches across projects. " +
        "Reports packages used at different versions with MAJOR/MINOR severity classification.")]
    public string CheckVersionAlignment()
        => _query.CheckVersionAlignment();

    [McpServerTool(Name = "find_untested_types"), Description(
        "Find production types (classes/records) that don't have matching test classes. " +
        "Uses naming convention heuristics: FooService → FooServiceTests/FooServiceTest.")]
    public string FindUntestedTypes(
        [Description("Filter by project name (optional)")] string? projectName = null)
        => _query.FindUntestedTypes(projectName);
}
