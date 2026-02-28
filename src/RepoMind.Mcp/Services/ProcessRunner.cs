using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RepoMind.Mcp.Services;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct = default);
}

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct = default)
    {
        logger.LogDebug("Running \"{FileName} {Arguments}\" in {WorkingDirectory}", fileName, arguments, workingDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(stderr))
            logger.LogWarning("Process exited with code {ExitCode}. Stderr: {Stderr}", process.ExitCode, stderr.Trim());

        logger.LogDebug("Process exited with code {ExitCode}", process.ExitCode);

        return new ProcessResult(process.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
