using System.ComponentModel;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class ScannerTools
{
    private readonly ScannerService _scanner;

    public ScannerTools(ScannerService scanner) => _scanner = scanner;

    [McpServerTool(Name = "rescan_memory"), Description(
        "Re-run the scanner to refresh the SQLite database with latest types and dependencies. " +
        "Use after pulling latest code. Set incremental=true to only rescan changed projects.")]
    public async Task<string> RescanMemory(
        [Description("When true, only rescan projects whose files changed since last scan")] bool incremental = false,
        CancellationToken ct = default)
    {
        var lastScan = _scanner.GetLastScanTime();
        var lastScanInfo = lastScan.HasValue
            ? $"Last scan: {lastScan.Value:yyyy-MM-dd HH:mm:ss} UTC"
            : "No previous scan found";

        var result = await _scanner.RescanMemory(incremental, ct);

        if (!result.Success)
            return $"❌ Scan failed after {result.Duration.TotalSeconds:F1}s\n\n{result.Output}\n\n{lastScanInfo}";

        return $"✅ Scan completed in {result.Duration.TotalSeconds:F1}s\n\n{result.Output}";
    }
}
