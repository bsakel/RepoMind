using RepoMind.Mcp.Configuration;
using Microsoft.Extensions.Logging;

namespace RepoMind.Mcp.Services;

public class GitService
{
    private readonly RepoMindConfiguration _config;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<GitService> _logger;

    public GitService(RepoMindConfiguration config, IProcessRunner processRunner, ILogger<GitService> logger)
    {
        _config = config;
        _processRunner = processRunner;
        _logger = logger;
    }

    public List<string> GetAllRepoDirectories()
    {
        return Directory.GetDirectories(_config.RootPath)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Where(d => Directory.Exists(Path.Combine(d, ".git")))
            .OrderBy(d => d)
            .ToList();
    }

    public async Task<string> GetBranchName(string repoPath, CancellationToken ct = default)
    {
        var result = await _processRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath, ct);
        return result.ExitCode == 0 ? result.StandardOutput : "unknown";
    }

    public async Task<RepoStatus> GetRepoStatus(string repoPath, CancellationToken ct = default)
    {
        var branch = await GetBranchName(repoPath, ct);

        var statusResult = await _processRunner.RunAsync("git", "status --porcelain", repoPath, ct);
        var hasChanges = statusResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(statusResult.StandardOutput);

        var aheadBehind = await _processRunner.RunAsync("git", "rev-list --left-right --count HEAD...@{upstream}", repoPath, ct);
        int ahead = 0, behind = 0;
        if (aheadBehind.ExitCode == 0 && !string.IsNullOrWhiteSpace(aheadBehind.StandardOutput))
        {
            var parts = aheadBehind.StandardOutput.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out ahead);
                int.TryParse(parts[1], out behind);
            }
        }

        return new RepoStatus(
            Path.GetFileName(repoPath),
            branch,
            hasChanges,
            ahead,
            behind
        );
    }

    public async Task<List<RepoStatus>> GetAllStatuses(CancellationToken ct = default)
    {
        var repos = GetAllRepoDirectories();
        _logger.LogInformation("Getting status for {RepoCount} repositories", repos.Count);
        var semaphore = new SemaphoreSlim(_config.MaxParallelism);

        var tasks = repos.Select(async repo =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                _logger.LogDebug("Getting status for repo {RepoName}", Path.GetFileName(repo));
                return await GetRepoStatus(repo, ct);
            }
            catch (Exception)
            {
                return new RepoStatus(Path.GetFileName(repo), "error", false, 0, 0);
            }
            finally
            {
                semaphore.Release();
            }
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    public async Task<PullResult> FetchAndPull(string repoPath, CancellationToken ct = default)
    {
        var name = Path.GetFileName(repoPath);
        var branch = await GetBranchName(repoPath, ct);

        if (!IsAllowedBranch(branch))
        {
            return new PullResult(name, branch, PullStatus.NonMasterBranch, $"On branch '{branch}' — skipped. Switch to master/main first.");
        }

        var fetchResult = await _processRunner.RunAsync("git", "fetch origin", repoPath, ct);
        if (fetchResult.ExitCode != 0)
        {
            _logger.LogWarning("Git fetch failed for {RepoName} with exit code {ExitCode}: {Error}", name, fetchResult.ExitCode, fetchResult.StandardError);
            return new PullResult(name, branch, PullStatus.Error, $"Fetch failed: {fetchResult.StandardError}");
        }

        var pullResult = await _processRunner.RunAsync("git", "pull", repoPath, ct);
        if (pullResult.ExitCode != 0)
        {
            _logger.LogWarning("Git pull failed for {RepoName} with exit code {ExitCode}: {Error}", name, pullResult.ExitCode, pullResult.StandardError);
            return new PullResult(name, branch, PullStatus.Error, $"Pull failed: {pullResult.StandardError}");
        }

        var message = pullResult.StandardOutput.Contains("Already up to date")
            ? "Already up to date."
            : "Updated.";

        return new PullResult(name, branch, PullStatus.Success, message);
    }

    public async Task<List<PullResult>> PullAllRepos(CancellationToken ct = default)
    {
        var repos = GetAllRepoDirectories();
        _logger.LogInformation("Pulling {RepoCount} repositories", repos.Count);
        var semaphore = new SemaphoreSlim(_config.MaxParallelism);

        var tasks = repos.Select(async repo =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                _logger.LogDebug("Pulling repo {RepoName}", Path.GetFileName(repo));
                return await FetchAndPull(repo, ct);
            }
            catch (Exception)
            {
                return new PullResult(Path.GetFileName(repo), "unknown", PullStatus.Error, "Unexpected error during pull.");
            }
            finally
            {
                semaphore.Release();
            }
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    private bool IsAllowedBranch(string branch)
    {
        return _config.AllowedBranches.Contains(branch, StringComparer.OrdinalIgnoreCase);
    }
}

public record RepoStatus(string Name, string Branch, bool HasUncommittedChanges, int Ahead, int Behind);

public enum PullStatus { Success, NonMasterBranch, Error }

public record PullResult(string Name, string Branch, PullStatus Status, string Message);
