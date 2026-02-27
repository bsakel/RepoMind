using RepoMind.Mcp.Configuration;
using RepoMind.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Resolve root path: REPOMIND_ROOT env var -> --root= CLI arg -> current directory
var rootPath = Environment.GetEnvironmentVariable("REPOMIND_ROOT");
if (string.IsNullOrEmpty(rootPath))
{
    var rootArg = args.FirstOrDefault(a => a.StartsWith("--root=", StringComparison.OrdinalIgnoreCase));
    rootPath = rootArg?.Substring("--root=".Length) ?? Directory.GetCurrentDirectory();
}

var config = new RepoMindConfiguration
{
    RootPath = rootPath,
    DbPath = Path.Combine(rootPath, "memory", "repomind.db")
};

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
            Version = "1.0.0"
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

await builder.Build().RunAsync();
