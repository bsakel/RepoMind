using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Resolve root path: REPOMIND_ROOT env var -> --root= CLI arg -> current directory
var rootPath = Environment.GetEnvironmentVariable("REPOMIND_ROOT");
if (string.IsNullOrEmpty(rootPath))
{
    var rootArg = args.FirstOrDefault(a => a.StartsWith("--root=", StringComparison.OrdinalIgnoreCase));
    rootPath = rootArg?.Substring("--root=".Length) ?? Directory.GetCurrentDirectory();
}

if (!Directory.Exists(rootPath))
{
    Console.Error.WriteLine($"Error: Root path does not exist: {rootPath}");
    return;
}

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
