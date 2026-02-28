using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RepoMind.Mcp.Services;
using ModelContextProtocol.Server;

namespace RepoMind.Mcp.Tools;

[McpServerToolType]
public class RepoTools
{
    private readonly GitService _git;
    private readonly ScannerService _scanner;
    private readonly ILogger<RepoTools> _logger;

    public RepoTools(GitService git, ScannerService scanner, ILogger<RepoTools> logger)
    {
        _git = git;
        _scanner = scanner;
        _logger = logger;
    }

    [McpServerTool(Name = "update_repos"), Description(
        "Fetch and pull latest code for all repos. " +
        "Runs up to 4 repos in parallel. Reports repos not on master branch. " +
        "Set autoRescan=true to trigger an incremental rescan after pulling.")]
    public async Task<string> UpdateRepos(
        [Description("When true, automatically rescan changed projects after pulling")] bool autoRescan = false,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "update_repos");
        _logger.LogDebug("Parameters: autoRescan={AutoRescan}", autoRescan);

        var results = await _git.PullAllRepos(ct);

        var lines = new List<string> { "| Project | Branch | Status | Details |", "| --- | --- | --- | --- |" };
        foreach (var r in results.OrderBy(r => r.Status).ThenBy(r => r.Name))
        {
            var statusIcon = r.Status switch
            {
                PullStatus.Success => "✅",
                PullStatus.NonMasterBranch => "⚠️",
                PullStatus.Error => "❌",
                _ => "?"
            };
            lines.Add($"| {r.Name} | {r.Branch} | {statusIcon} | {r.Message} |");
        }

        var successCount = results.Count(r => r.Status == PullStatus.Success);
        var warnCount = results.Count(r => r.Status == PullStatus.NonMasterBranch);
        var errorCount = results.Count(r => r.Status == PullStatus.Error);
        var changedCount = results.Count(r => r.Status == PullStatus.Success && r.Message == "Updated.");

        lines.Add("");
        lines.Add($"**Summary:** {successCount} updated, {warnCount} skipped (non-master), {errorCount} errors, {changedCount} changed");

        // Auto-rescan if requested and there were changes
        if (autoRescan && changedCount > 0)
        {
            var scanResult = await _scanner.RescanMemory(incremental: true, ct);
            lines.Add("");
            lines.Add(scanResult.Success
                ? $"🔄 **Auto-rescan:** {scanResult.Output}"
                : $"❌ **Auto-rescan failed:** {scanResult.Output}");
        }
        else if (autoRescan && changedCount == 0)
        {
            lines.Add("");
            lines.Add("🔄 **Auto-rescan:** Skipped (no repos changed)");
        }

        return string.Join("\n", lines);
    }

    [McpServerTool(Name = "get_repo_status"), Description(
        "Show git status for all repos: " +
        "current branch, uncommitted changes, ahead/behind remote.")]
    public async Task<string> GetRepoStatus(CancellationToken ct = default)
    {
        _logger.LogInformation("Tool {ToolName} invoked", "get_repo_status");

        var statuses = await _git.GetAllStatuses(ct);

        var lines = new List<string> { "| Project | Branch | Changes | Ahead | Behind |", "| --- | --- | --- | --- | --- |" };
        foreach (var s in statuses.OrderBy(s => s.Name))
        {
            var changes = s.HasUncommittedChanges ? "⚠️ dirty" : "clean";
            lines.Add($"| {s.Name} | {s.Branch} | {changes} | {s.Ahead} | {s.Behind} |");
        }

        var dirtyCount = statuses.Count(s => s.HasUncommittedChanges);
        var nonMaster = statuses.Count(s => s.Branch != "master" && s.Branch != "main");
        lines.Add("");
        lines.Add($"**Summary:** {statuses.Count} repos, {dirtyCount} with uncommitted changes, {nonMaster} not on master/main");

        return string.Join("\n", lines);
    }
}
