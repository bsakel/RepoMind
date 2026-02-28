using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class ScannerTools
{
    private readonly ScannerService _scanner;
    private readonly ILogger<ScannerTools> _logger;

    public ScannerTools(ScannerService scanner, ILogger<ScannerTools> logger)
    {
        _scanner = scanner;
        _logger = logger;
    }

    [McpServerTool(Name = "rescan_project"), Description(
        "Rescan a specific project to refresh its data in the SQLite database. " +
        "Invalidates the project's cached hash and runs an incremental scan, so only the specified project is re-processed.")]
    public async Task<string> RescanProject(
        [Description("Name of the project (directory name under the codebase root) to rescan")] string projectName,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Tool {ToolName} invoked with project={Project}", "rescan_project", projectName);

        var result = await _scanner.RescanProject(projectName, ct);

        if (!result.Success)
            return $"❌ Rescan failed for '{projectName}': {result.Output}";

        return $"✅ {result.Output}";
    }

    [McpServerTool(Name = "rescan_memory"), Description(
        "Re-run the scanner to refresh the SQLite database with latest types and dependencies. " +
        "Use after pulling latest code. Set incremental=true to only rescan changed projects.")]
    public async Task<string> RescanMemory(
        [Description("When true, only rescan projects whose files changed since last scan")] bool incremental = false,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "rescan_memory");
        _logger.LogDebug("Parameters: incremental={Incremental}", incremental);

        var lastScan = _scanner.GetLastScanTime();
        var lastScanInfo = lastScan.HasValue
            ? $"Last scan: {lastScan.Value:yyyy-MM-dd HH:mm:ss} UTC"
            : "No previous scan found";

        var result = await _scanner.RescanMemory(incremental, ct);

        if (!result.Success)
            return $"❌ Scan failed after {result.Duration.TotalSeconds:F1}s\n\n{result.Output}\n\n{lastScanInfo}";

        return $"✅ Scan completed in {result.Duration.TotalSeconds:F1}s\n\n{result.Output}\n\n{lastScanInfo}";
    }
}
