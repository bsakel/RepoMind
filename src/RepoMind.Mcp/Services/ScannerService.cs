using RepoMind.Mcp.Configuration;
using RepoMind.Scanner;
using RepoMind.Scanner.Writers;
using Microsoft.Extensions.Logging;

namespace RepoMind.Mcp.Services;

public class ScannerService
{
    private readonly RepoMindConfiguration _config;
    private readonly ILogger<ScannerService> _logger;
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public ScannerService(RepoMindConfiguration config, ILogger<ScannerService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ScanResult> RescanMemory(bool incremental = false, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting rescan (incremental: {Incremental}) for {RootPath}", incremental, _config.RootPath);
        await _scanLock.WaitAsync(ct);
        try
        {
            var result = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var outputDir = Path.GetDirectoryName(_config.DbPath) ?? Path.Combine(_config.RootPath, "memory");

                var options = new ScanOptions(
                    _config.RootPath,
                    outputDir,
                    SqliteOnly: true,
                    Incremental: incremental);

                var engine = new ScannerEngine();
                var summary = engine.Run(options);

                ct.ThrowIfCancellationRequested();

                var msg = summary.Success
                    ? $"Scanned {summary.ProjectCount} projects, {summary.TypeCount} types in {summary.ElapsedSeconds:F1}s" +
                      (summary.SkippedCount > 0 ? $" ({summary.SkippedCount} unchanged, skipped)" : "") +
                      (summary.FailedProjects?.Count > 0 ? $"\n⚠️ {summary.FailedProjects.Count} project(s) failed:\n" +
                          string.Join("\n", summary.FailedProjects.Select(f => $"  • {f.ProjectName}: {f.Error}")) : "")
                    : $"Scanner failed: {summary.Error}";

                return new ScanResult(summary.Success, msg, TimeSpan.FromSeconds(summary.ElapsedSeconds));
            }, ct);

            _logger.LogInformation("Rescan completed in {Duration}", result.Duration);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Rescan failed for {RootPath}", _config.RootPath);
            throw;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    public async Task<ScanResult> RescanProject(string projectName, CancellationToken ct = default)
    {
        var projectDir = Path.Combine(_config.RootPath, projectName);
        if (!Directory.Exists(projectDir))
            return new ScanResult(false, $"Project directory not found: {projectName}", TimeSpan.Zero);

        if (!Directory.Exists(Path.Combine(projectDir, ".git")))
            return new ScanResult(false, $"Not a git repository: {projectName}", TimeSpan.Zero);

        _logger.LogInformation("Starting per-project rescan for {Project} under {RootPath}", projectName, _config.RootPath);

        await _scanLock.WaitAsync(ct);
        try
        {
            var result = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var outputDir = Path.GetDirectoryName(_config.DbPath) ?? Path.Combine(_config.RootPath, "memory");

                // Use incremental scan — the engine will pick up changes for this project
                // and skip all unchanged projects, making this effectively project-scoped.
                var options = new ScanOptions(
                    _config.RootPath,
                    outputDir,
                    SqliteOnly: true,
                    Incremental: true);

                // Clear the stored hash for this project so the engine treats it as changed
                var dbPath = Path.Combine(outputDir, "repomind.db");
                if (File.Exists(dbPath))
                    SqliteWriter.DeleteScanHash(dbPath, projectName);

                var engine = new ScannerEngine();
                var summary = engine.Run(options);

                ct.ThrowIfCancellationRequested();

                var msg = summary.Success
                    ? $"Rescanned project '{projectName}': {summary.ProjectCount} projects processed, {summary.TypeCount} types in {summary.ElapsedSeconds:F1}s" +
                      (summary.SkippedCount > 0 ? $" ({summary.SkippedCount} unchanged, skipped)" : "") +
                      (summary.FailedProjects?.Count > 0 ? $"\n⚠️ {summary.FailedProjects.Count} project(s) failed:\n" +
                          string.Join("\n", summary.FailedProjects.Select(f => $"  • {f.ProjectName}: {f.Error}")) : "")
                    : $"Scanner failed: {summary.Error}";

                return new ScanResult(summary.Success, msg, TimeSpan.FromSeconds(summary.ElapsedSeconds));
            }, ct);

            _logger.LogInformation("Per-project rescan for {Project} completed in {Duration}", projectName, result.Duration);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Per-project rescan failed for {Project}", projectName);
            throw;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    public DateTime? GetLastScanTime()
    {
        if (!File.Exists(_config.DbPath))
            return null;
        return File.GetLastWriteTimeUtc(_config.DbPath);
    }
}

public record ScanResult(bool Success, string Output, TimeSpan Duration);
