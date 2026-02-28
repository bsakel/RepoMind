using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using RepoMind.Scanner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Serilog;
using System.Diagnostics;

// Resolve root path: REPOMIND_ROOT env var -> --root= CLI arg -> current directory
var rootPath = Environment.GetEnvironmentVariable("REPOMIND_ROOT");
if (string.IsNullOrEmpty(rootPath))
{
    var rootArg = args.FirstOrDefault(a => a.StartsWith("--root=", StringComparison.OrdinalIgnoreCase));
    rootPath = rootArg?.Substring("--root=".Length) ?? Directory.GetCurrentDirectory();
}

// Handle --init: validate environment, scan codebase, output summary, then exit
if (args.Contains("--init"))
{
    await RunInit(rootPath, args);
    return;
}

if (!Directory.Exists(rootPath))
{
    Console.Error.WriteLine($"Error: Root path does not exist: {rootPath}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

var config = new RepoMindConfiguration
{
    RootPath = rootPath,
    DbPath = Path.Combine(rootPath, "memory", "repomind.db")
};

var maxParallelismEnv = Environment.GetEnvironmentVariable("REPOMIND_MAX_PARALLELISM");
if (int.TryParse(maxParallelismEnv, out var maxPar) && maxPar > 0)
    config.MaxParallelism = maxPar;

var branchesEnv = Environment.GetEnvironmentVariable("REPOMIND_ALLOWED_BRANCHES");
if (!string.IsNullOrEmpty(branchesEnv))
    config.AllowedBranches = branchesEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

builder.Services.Configure<RepoMindConfiguration>(opts =>
{
    opts.RootPath = config.RootPath;
    opts.DbPath = config.DbPath;
    opts.MaxParallelism = config.MaxParallelism;
    opts.AllowedBranches = config.AllowedBranches;
});
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<QueryService>();
builder.Services.AddSingleton<GitService>();
builder.Services.AddSingleton<ScannerService>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "RepoMind",
            Version = "0.1.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Logging to stderr (stdout is reserved for MCP protocol)
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts =>
{
    opts.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Bridge Serilog static API to stderr so Scanner's in-process Log.* calls are captured
Serilog.Log.Logger = new Serilog.LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

await builder.Build().RunAsync();

// --init implementation: validates environment and runs first scan
static async Task RunInit(string rootPath, string[] args)
{
    Console.WriteLine("╔══════════════════════════════════╗");
    Console.WriteLine("║       RepoMind — Init            ║");
    Console.WriteLine("╚══════════════════════════════════╝");
    Console.WriteLine();

    // 1. Validate root path
    if (!Directory.Exists(rootPath))
    {
        Console.Error.WriteLine($"✗ Root path does not exist: {rootPath}");
        Console.Error.WriteLine("  Use --root=<path> or set REPOMIND_ROOT environment variable.");
        return;
    }
    Console.WriteLine($"✓ Root path: {rootPath}");

    // 2. Validate git
    try
    {
        var psi = new ProcessStartInfo("git", "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        var gitVersion = proc?.StandardOutput.ReadToEnd().Trim();
        proc?.WaitForExit();
        if (proc?.ExitCode == 0)
            Console.WriteLine($"✓ Git: {gitVersion}");
        else
            Console.Error.WriteLine("✗ Git is not available. Install git and try again.");
    }
    catch
    {
        Console.Error.WriteLine("✗ Git is not available. Install git and try again.");
        return;
    }

    // 3. Detect repositories
    var repos = Directory.GetDirectories(rootPath)
        .Where(d => !Path.GetFileName(d).StartsWith(".") && Directory.Exists(Path.Combine(d, ".git")))
        .ToList();

    if (repos.Count == 0)
    {
        Console.Error.WriteLine($"✗ No git repositories found under {rootPath}");
        Console.Error.WriteLine("  RepoMind scans immediate subdirectories that contain a .git folder.");
        return;
    }
    Console.WriteLine($"✓ Found {repos.Count} git repositories:");
    foreach (var repo in repos)
        Console.WriteLine($"    • {Path.GetFileName(repo)}");

    Console.WriteLine();

    // 4. Initialize Serilog for scanner progress output
    Serilog.Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    // 5. Run scanner
    var outputDir = Path.Combine(rootPath, "memory");
    var incremental = args.Contains("--incremental");
    Console.WriteLine($"Scanning {repos.Count} repositories{(incremental ? " (incremental)" : "")}...");
    Console.WriteLine();

    var options = new ScanOptions(rootPath, outputDir, Incremental: incremental);
    var engine = new ScannerEngine();
    var summary = engine.Run(options);

    Serilog.Log.CloseAndFlush();

    // 6. Print summary
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════");
    if (summary.Success)
    {
        Console.WriteLine($"✓ Scan complete in {summary.ElapsedSeconds:F1}s");
        Console.WriteLine($"  Projects scanned: {summary.ProjectCount}");
        if (summary.SkippedCount > 0)
            Console.WriteLine($"  Projects skipped: {summary.SkippedCount} (unchanged)");
        if (summary.FailedProjects?.Count > 0)
        {
            Console.WriteLine($"  Projects failed:  {summary.FailedProjects.Count}");
            foreach (var f in summary.FailedProjects)
                Console.WriteLine($"    ✗ {f.ProjectName}: {f.Error}");
        }
        Console.WriteLine($"  Types discovered: {summary.TypeCount}");
        Console.WriteLine($"  Database: {Path.Combine(outputDir, "repomind.db")}");
        Console.WriteLine();
        Console.WriteLine("Memory is ready. Configure your MCP client to use RepoMind:");
        Console.WriteLine("  {");
        Console.WriteLine("    \"mcpServers\": {");
        Console.WriteLine("      \"repomind\": {");
        Console.WriteLine($"        \"command\": \"dotnet\",");
        Console.WriteLine($"        \"args\": [\"run\", \"--project\", \"<path-to-RepoMind.Mcp>\", \"--\", \"--root={rootPath}\"]");
        Console.WriteLine("      }");
        Console.WriteLine("    }");
        Console.WriteLine("  }");
    }
    else
    {
        Console.Error.WriteLine($"✗ Scan failed: {summary.Error}");
    }
    Console.WriteLine("═══════════════════════════════════");
}
