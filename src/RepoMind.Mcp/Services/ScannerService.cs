using RepoMind.Mcp.Configuration;
using RepoMind.Scanner;

namespace RepoMind.Mcp.Services;

public class ScannerService
{
    private readonly RepoMindConfiguration _config;

    public ScannerService(RepoMindConfiguration config)
    {
        _config = config;
    }

    public Task<ScanResult> RescanMemory(bool incremental = false, CancellationToken ct = default)
    {
        var outputDir = Path.GetDirectoryName(_config.DbPath) ?? Path.Combine(_config.RootPath, "memory");

        var options = new ScanOptions(
            _config.RootPath,
            outputDir,
            SqliteOnly: true,
            Incremental: incremental);

        var engine = new ScannerEngine();
        var summary = engine.Run(options);

        var msg = summary.Success
            ? $"Scanned {summary.ProjectCount} projects, {summary.TypeCount} types in {summary.ElapsedSeconds:F1}s" +
              (summary.SkippedCount > 0 ? $" ({summary.SkippedCount} unchanged, skipped)" : "")
            : $"Scanner failed: {summary.Error}";

        var result = new ScanResult(summary.Success, msg, TimeSpan.FromSeconds(summary.ElapsedSeconds));
        return Task.FromResult(result);
    }

    public DateTime? GetLastScanTime()
    {
        if (!File.Exists(_config.DbPath))
            return null;
        return File.GetLastWriteTimeUtc(_config.DbPath);
    }
}

public record ScanResult(bool Success, string Output, TimeSpan Duration);
