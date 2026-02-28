using RepoMind.Scanner;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    if (args.Contains("--help") || args.Contains("-h"))
    {
        Log.Information("Usage: dotnet run -- [--root <path>] [--output <dir>] [--sqlite-only] [--flat-only] [--incremental]");
        return 0;
    }

    var rootPath = args.Length >= 2 && args[0] == "--root" ? args[1] : Directory.GetCurrentDirectory();
    var outputDir = args.Length >= 4 && args[2] == "--output" ? args[3] : Path.Combine(rootPath, "memory");

    Log.Information("RepoMind Scanner");
    Log.Information("Root: {Root}", rootPath);
    Log.Information("Output: {Output}", outputDir);

    var options = new ScanOptions(
        rootPath,
        outputDir,
        SqliteOnly: args.Contains("--sqlite-only"),
        FlatOnly: args.Contains("--flat-only"),
        Incremental: args.Contains("--incremental"));

    var engine = new ScannerEngine();
    var result = engine.Run(options);

    return result.Success ? 0 : 1;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Scanner failed");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
