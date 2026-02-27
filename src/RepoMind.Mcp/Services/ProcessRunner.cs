using System.Diagnostics;

namespace RepoMind.Mcp.Services;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct = default);
}

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct = default)
    {
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

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        return new ProcessResult(process.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
