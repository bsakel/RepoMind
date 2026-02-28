using System.Diagnostics;
using RepoMind.Scanner.Models;
using RepoMind.Scanner.Parsers;
using RepoMind.Scanner.Writers;
using Serilog;

namespace RepoMind.Scanner;

public record ScanOptions(string RootPath, string OutputDir, bool SqliteOnly = false, bool FlatOnly = false, bool Incremental = false);

public record ProjectFailure(string ProjectName, string Error);

public record ScanSummary(int ProjectCount, int TypeCount, double ElapsedSeconds, bool Success, string? Error = null, int SkippedCount = 0, List<ProjectFailure>? FailedProjects = null);

/// <summary>
/// Core scanning engine that can be called in-process from other projects.
/// </summary>
public class ScannerEngine
{
    public ScanSummary Run(ScanOptions options)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var projects = new List<ProjectInfo>();
            var assembliesByProject = new Dictionary<string, List<AssemblyInfo>>();
            var typesByProject = new Dictionary<string, List<TypeInfo>>();
            var configByProject = new Dictionary<string, List<ConfigEntry>>();

            var directories = Directory.GetDirectories(options.RootPath)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .OrderBy(d => d)
                .ToList();

            Log.Information("Found {Count} directories to scan", directories.Count);

            var dbPath = Path.Combine(options.OutputDir, "repomind.db");
            var existingHashes = new Dictionary<string, string>();
            var computedHashes = new Dictionary<string, string>();
            var skippedCount = 0;
            var failedProjects = new List<ProjectFailure>();

            if (options.Incremental && File.Exists(dbPath))
            {
                existingHashes = SqliteWriter.ReadScanHashes(dbPath);
                Log.Information("Incremental mode: loaded {Count} existing project hashes", existingHashes.Count);
            }

            var scannableCount = directories.Count;
            var currentIndex = 0;

            foreach (var dir in directories)
            {
                var name = Path.GetFileName(dir);
                var gitDir = Path.Combine(dir, ".git");
                if (!Directory.Exists(gitDir))
                {
                    scannableCount--;
                    Log.Debug("[{Project}] Skipped (not a git repo)", name);
                    continue;
                }

                currentIndex++;

                // Incremental: skip unchanged projects
                if (options.Incremental && existingHashes.Count > 0)
                {
                    var currentHash = ComputeProjectHash(dir);
                    computedHashes[name] = currentHash;
                    if (existingHashes.TryGetValue(name, out var storedHash) && storedHash == currentHash)
                    {
                        Log.Information("[{Index}/{Total}] {Project} — skipped (unchanged)", currentIndex, scannableCount, name);
                        skippedCount++;
                        continue;
                    }
                }

                Log.Information("[{Index}/{Total}] Scanning {Project}...", currentIndex, scannableCount, name);
                var projectSw = Stopwatch.StartNew();

                try
                {
                    var slnFile = Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    var slnName = slnFile != null ? Path.GetFileName(slnFile) : null;

                    string? remote = null;
                    try
                    {
                        var psi = new ProcessStartInfo("git", "remote get-url origin")
                        {
                            WorkingDirectory = dir,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        remote = proc?.StandardOutput.ReadToEnd().Trim();
                        proc?.WaitForExit();
                        if (proc?.ExitCode != 0) remote = null;
                        if (remote != null)
                            remote = SanitizeGitUrl(remote);
                    }
                    catch { /* ignore */ }

                    var defaultBranch = DetectDefaultBranch(dir);
                    var project = new ProjectInfo(name, dir, slnName, remote, defaultBranch);
                    projects.Add(project);

                    var assemblies = CsprojParser.ParseProject(dir, name);
                    assembliesByProject[name] = assemblies;

                    var types = RoslynScanner.ScanSourceFiles(dir);
                    typesByProject[name] = types;

                    var configEntries = ConfigParser.ScanProject(dir);
                    configByProject[name] = configEntries;

                    projectSw.Stop();
                    Log.Information("[{Index}/{Total}] {Project} — {AsmCount} assemblies, {TypeCount} types, {ConfigCount} config keys ({Elapsed:F1}s)",
                        currentIndex, scannableCount, name, assemblies.Count, types.Count, configEntries.Count, projectSw.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    projectSw.Stop();
                    Log.Error(ex, "[{Index}/{Total}] {Project} — FAILED ({Elapsed:F1}s): {Error}",
                        currentIndex, scannableCount, name, projectSw.Elapsed.TotalSeconds, ex.Message);
                    failedProjects.Add(new ProjectFailure(name, ex.Message));
                }
            }

            // Resolve internal packages by matching against all known assembly names
            CsprojParser.ResolveInternalPackages(assembliesByProject);

            Directory.CreateDirectory(options.OutputDir);

            if (!options.FlatOnly)
            {
                Log.Information("Writing SQLite database: {Path}", dbPath);

                var isIncremental = options.Incremental && File.Exists(dbPath) && projects.Count > 0;
                using var writer = isIncremental
                    ? SqliteWriter.OpenExisting(dbPath)
                    : new SqliteWriter(dbPath);

                foreach (var project in projects)
                {
                    if (isIncremental)
                        writer.DeleteProject(project.Name);

                    var projectId = writer.InsertProject(project);
                    var assemblies = assembliesByProject.GetValueOrDefault(project.Name, []);
                    var types = typesByProject.GetValueOrDefault(project.Name, []);

                    foreach (var asm in assemblies)
                    {
                        var asmId = writer.InsertAssembly(projectId, asm);
                        var asmDir = Path.GetDirectoryName(asm.CsprojPath)?.Replace('\\', '/') ?? "";
                        var asmTypes = types.Where(t =>
                            t.FilePath.Replace('\\', '/').StartsWith(asmDir, StringComparison.OrdinalIgnoreCase)).ToList();

                        if (asmTypes.Count > 0)
                            writer.InsertTypes(asmId, asmTypes);
                    }

                    // Insert config keys from pre-collected data
                    var configEntries = configByProject.GetValueOrDefault(project.Name, []);
                    if (configEntries.Count > 0)
                        writer.InsertConfigKeys(project.Name, configEntries);

                    // Store hash for incremental scanning (reuse if already computed)
                    if (!computedHashes.TryGetValue(project.Name, out var hash))
                        hash = ComputeProjectHash(project.DirectoryPath);
                    writer.UpsertScanHash(project.Name, hash);
                }

                Log.Information("SQLite database written with {Projects} projects ({Skipped} unchanged)",
                    projects.Count, skippedCount);
            }

            if (!options.SqliteOnly)
            {
                if (options.Incremental)
                    FlatFileWriter.MergeAll(options.OutputDir, projects, assembliesByProject, typesByProject, configByProject);
                else
                    FlatFileWriter.WriteAll(options.OutputDir, projects, assembliesByProject, typesByProject, configByProject);
            }

            sw.Stop();
            var totalTypes = typesByProject.Values.Sum(t => t.Count);

            if (failedProjects.Count > 0)
            {
                Log.Warning("Scan completed with {FailCount} failure(s):", failedProjects.Count);
                foreach (var f in failedProjects)
                    Log.Warning("  ✗ {Project}: {Error}", f.ProjectName, f.Error);
            }

            Log.Information("Scan complete in {Elapsed}s. {Projects} projects scanned, {Skipped} skipped, {Failed} failed, {Types} total types.",
                sw.Elapsed.TotalSeconds.ToString("F1"), projects.Count, skippedCount, failedProjects.Count, totalTypes);

            return new ScanSummary(projects.Count, totalTypes, sw.Elapsed.TotalSeconds, true,
                SkippedCount: skippedCount, FailedProjects: failedProjects.Count > 0 ? failedProjects : null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Scanner failed");
            return new ScanSummary(0, 0, sw.Elapsed.TotalSeconds, false, ex.Message);
        }
    }

    /// <summary>
    /// Detect the default branch for a git repository.
    /// </summary>
    internal static string DetectDefaultBranch(string repoDir)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "symbolic-ref refs/remotes/origin/HEAD --short")
            {
                WorkingDirectory = repoDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();
            if (proc?.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                // Strip "origin/" prefix
                const string prefix = "origin/";
                return output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? output[prefix.Length..]
                    : output;
            }
        }
        catch { /* ignore */ }

        return "main";
    }

    /// <summary>
    /// Strip embedded credentials from a git remote URL.
    /// Converts "https://user:pass@host/repo" to "https://host/repo".
    /// </summary>
    internal static string SanitizeGitUrl(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return url;

        var afterScheme = schemeEnd + 3;
        var atIndex = url.IndexOf('@', afterScheme);
        if (atIndex < 0) return url;

        return string.Concat(url.AsSpan(0, afterScheme), url.AsSpan(atIndex + 1));
    }

    /// <summary>
    /// Compute a hash of all .cs and .csproj files' last-write times and sizes.
    /// </summary>
    internal static string ComputeProjectHash(string projectDir)
    {
        var entries = new List<string>();
        var patterns = new[] { "*.cs", "*.csproj" };

        foreach (var pattern in patterns)
        {
            foreach (var file in Directory.GetFiles(projectDir, pattern, SearchOption.AllDirectories))
            {
                // Exclude bin/obj directories
                var relativePath = Path.GetRelativePath(projectDir, file);
                if (relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    relativePath.StartsWith($"bin{Path.DirectorySeparatorChar}") ||
                    relativePath.StartsWith($"obj{Path.DirectorySeparatorChar}"))
                    continue;

                var info = new FileInfo(file);
                entries.Add($"{relativePath}|{info.LastWriteTimeUtc.Ticks}|{info.Length}");
            }
        }

        entries.Sort(StringComparer.OrdinalIgnoreCase);
        var combined = string.Join("\n", entries);
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(bytes);
    }
}
