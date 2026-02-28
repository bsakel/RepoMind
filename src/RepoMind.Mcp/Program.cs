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

// Handle --doctor: diagnose RepoMind setup and optionally investigate a missing type
if (args.Contains("--doctor"))
{
    RunDoctor(rootPath, args);
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

// --doctor implementation: diagnose setup and investigate missing types
static void RunDoctor(string rootPath, string[] args)
{
    Console.WriteLine("╔══════════════════════════════════╗");
    Console.WriteLine("║      RepoMind — Doctor           ║");
    Console.WriteLine("╚══════════════════════════════════╝");
    Console.WriteLine();

    var allGood = true;

    // 1. Check root path
    if (Directory.Exists(rootPath))
        Console.WriteLine($"✓ Root path exists: {rootPath}");
    else
    {
        Console.Error.WriteLine($"✗ Root path does not exist: {rootPath}");
        allGood = false;
    }

    // 2. Check git
    try
    {
        var psi = new ProcessStartInfo("git", "--version")
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        Console.WriteLine(proc?.ExitCode == 0 ? "✓ Git is available" : "✗ Git is not available");
        if (proc?.ExitCode != 0) allGood = false;
    }
    catch { Console.Error.WriteLine("✗ Git is not available"); allGood = false; }

    // 3. Check memory directory
    var memoryDir = Path.Combine(rootPath, "memory");
    if (Directory.Exists(memoryDir))
        Console.WriteLine($"✓ Memory directory exists: {memoryDir}");
    else
    {
        Console.Error.WriteLine($"✗ Memory directory not found: {memoryDir}");
        Console.Error.WriteLine("  Run 'repomind --init' to create it.");
        allGood = false;
    }

    // 4. Check database
    var dbPath = Path.Combine(memoryDir, "repomind.db");
    if (File.Exists(dbPath))
    {
        Console.WriteLine($"✓ Database exists: {dbPath}");
        var fileSize = new FileInfo(dbPath).Length;
        Console.WriteLine($"  Size: {fileSize / 1024.0:F1} KB");
    }
    else
    {
        Console.Error.WriteLine($"✗ Database not found: {dbPath}");
        Console.Error.WriteLine("  Run 'repomind --init' to scan your codebase.");
        allGood = false;
        return;
    }

    // 5. Check database tables and contents
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
    try
    {
        conn.Open();
        Console.WriteLine("✓ Database is readable");

        var tables = new[] { "projects", "assemblies", "namespaces", "types", "methods", "endpoints", "type_interfaces", "type_injected_deps", "package_references", "config_entries" };
        var missingTables = new List<string>();
        foreach (var table in tables)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
            var exists = (long)(cmd.ExecuteScalar() ?? 0) > 0;
            if (!exists) missingTables.Add(table);
        }

        if (missingTables.Count == 0)
            Console.WriteLine($"✓ All {tables.Length} expected tables present");
        else
        {
            Console.Error.WriteLine($"✗ Missing tables: {string.Join(", ", missingTables)}");
            Console.Error.WriteLine("  Database may be corrupt. Run 'repomind --init' to rescan.");
            allGood = false;
        }

        // 6. Check data counts
        long projectCount = 0, typeCount = 0, endpointCount = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM projects";
            projectCount = (long)(cmd.ExecuteScalar() ?? 0);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM types";
            typeCount = (long)(cmd.ExecuteScalar() ?? 0);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM endpoints";
            endpointCount = (long)(cmd.ExecuteScalar() ?? 0);
        }

        Console.WriteLine($"  Projects: {projectCount}");
        Console.WriteLine($"  Types:    {typeCount}");
        Console.WriteLine($"  Endpoints: {endpointCount}");

        if (projectCount == 0)
        {
            Console.Error.WriteLine("✗ No projects in database — scan may have failed");
            allGood = false;
        }
        else
            Console.WriteLine("✓ Database has data");

        // 7. Check scan freshness
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='scan_metadata'";
            if (cmd.ExecuteScalar() != null)
            {
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = "SELECT MAX(last_scan_utc) FROM scan_metadata";
                var lastScan = cmd2.ExecuteScalar();
                if (lastScan != null && lastScan != DBNull.Value)
                    Console.WriteLine($"  Last scan: {lastScan}");
            }
        }

        // 8. Check flat files
        var flatFiles = new[] { "projects.json", "dependency-graph.json", "types-index.json" };
        var missingFlat = flatFiles.Where(f => !File.Exists(Path.Combine(memoryDir, f))).ToList();
        if (missingFlat.Count == 0)
            Console.WriteLine($"✓ All {flatFiles.Length} flat files present");
        else
            Console.WriteLine($"⚠ Missing flat files: {string.Join(", ", missingFlat)}");

        // 9. Type investigation (--type=TypeName)
        var typeArg = args.FirstOrDefault(a => a.StartsWith("--type=", StringComparison.OrdinalIgnoreCase));
        if (typeArg != null)
        {
            var targetType = typeArg.Substring("--type=".Length);
            Console.WriteLine();
            Console.WriteLine($"═══ Investigating type: {targetType} ═══");
            Console.WriteLine();

            // Check if type exists
            using var findCmd = conn.CreateCommand();
            findCmd.CommandText = @"
                SELECT t.type_name, t.kind, t.is_public, t.file_path, n.namespace_name, a.assembly_name, p.name as project_name
                FROM types t
                JOIN namespaces n ON t.namespace_id = n.id
                JOIN assemblies a ON n.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id
                WHERE t.type_name LIKE @name";
            findCmd.Parameters.AddWithValue("@name", $"%{targetType}%");
            using var reader = findCmd.ExecuteReader();
            var found = false;
            while (reader.Read())
            {
                found = true;
                Console.WriteLine($"✓ Found: {reader.GetString(4)}.{reader.GetString(0)}");
                Console.WriteLine($"  Kind:     {reader.GetString(1)}");
                Console.WriteLine($"  Public:   {(reader.GetBoolean(2) ? "yes" : "no")}");
                Console.WriteLine($"  File:     {reader.GetString(3)}");
                Console.WriteLine($"  Assembly: {reader.GetString(5)}");
                Console.WriteLine($"  Project:  {reader.GetString(6)}");
                Console.WriteLine();
            }

            if (!found)
            {
                Console.Error.WriteLine($"✗ Type '{targetType}' not found in the database");
                Console.WriteLine();
                Console.WriteLine("Possible reasons:");
                Console.WriteLine("  • The type is not public (only public types are scanned)");
                Console.WriteLine("  • The file is in a test/benchmark project (excluded by default)");
                Console.WriteLine("  • The project containing it was not detected as a git repo");
                Console.WriteLine("  • The file failed to parse (check scanner output)");
                Console.WriteLine("  • The type is defined in a file excluded by obj/bin filters");

                // Check if project is scanned
                Console.WriteLine();
                Console.WriteLine("Scanned projects:");
                using var projCmd = conn.CreateCommand();
                projCmd.CommandText = "SELECT name FROM projects ORDER BY name";
                using var projReader = projCmd.ExecuteReader();
                while (projReader.Read())
                    Console.WriteLine($"  • {projReader.GetString(0)}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"✗ Cannot read database: {ex.Message}");
        allGood = false;
    }

    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════");
    Console.WriteLine(allGood ? "✓ RepoMind is healthy!" : "⚠ Issues found. See details above.");
    Console.WriteLine("═══════════════════════════════════");
}
