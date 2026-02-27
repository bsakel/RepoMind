using System.Xml.Linq;
using RepoMind.Scanner.Models;
using Serilog;

namespace RepoMind.Scanner.Parsers;

public static class CsprojParser
{
    public static List<AssemblyInfo> ParseProject(string projectDir, string projectName)
    {
        var results = new List<AssemblyInfo>();
        var csprojFiles = Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories);

        foreach (var csproj in csprojFiles)
        {
            var relativePath = Path.GetRelativePath(projectDir, csproj);
            var isTest = IsTestProject(relativePath, csproj);
            if (isTest) continue;

            try
            {
                var info = ParseCsproj(csproj, projectName, relativePath);
                results.Add(info);
                Log.Information("  Parsed assembly: {Assembly} ({Refs} package refs)",
                    info.AssemblyName, info.PackageReferences.Count);
            }
            catch (Exception ex)
            {
                Log.Warning("  Failed to parse {Csproj}: {Error}", relativePath, ex.Message);
            }
        }

        return results;
    }

    /// <summary>
    /// After all projects are scanned, marks package references as internal
    /// if their name matches any known assembly name in the codebase.
    /// </summary>
    public static void ResolveInternalPackages(Dictionary<string, List<AssemblyInfo>> assembliesByProject)
    {
        var allAssemblyNames = new HashSet<string>(
            assembliesByProject.Values.SelectMany(list => list).Select(a => a.AssemblyName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var assemblies in assembliesByProject.Values)
        {
            for (int i = 0; i < assemblies.Count; i++)
            {
                var asm = assemblies[i];
                var updatedRefs = asm.PackageReferences
                    .Select(p => p with { IsInternal = allAssemblyNames.Contains(p.Name) })
                    .ToList();
                assemblies[i] = asm with { PackageReferences = updatedRefs };
            }
        }
    }

    private static AssemblyInfo ParseCsproj(string csprojPath, string projectName, string relativePath)
    {
        var doc = XDocument.Load(csprojPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var props = doc.Descendants(ns + "PropertyGroup");
        var targetFramework = props.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value
                           ?? props.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value;
        var outputType = props.Descendants(ns + "OutputType").FirstOrDefault()?.Value;
        var assemblyName = props.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value
                        ?? Path.GetFileNameWithoutExtension(csprojPath);

        var packageRefs = doc.Descendants(ns + "PackageReference")
            .Select(e => new PackageRef(
                e.Attribute("Include")?.Value ?? "",
                e.Attribute("Version")?.Value ?? e.Descendants(ns + "Version").FirstOrDefault()?.Value,
                false))
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .ToList();

        var projectRefs = doc.Descendants(ns + "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? "")
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        return new AssemblyInfo(
            projectName, relativePath, assemblyName,
            targetFramework, outputType, false,
            packageRefs, projectRefs);
    }

    private static bool IsTestProject(string relativePath, string csprojPath)
    {
        var pathLower = relativePath.Replace('\\', '/').ToLowerInvariant();
        if (pathLower.Contains("/test/") || pathLower.Contains("/tests/") || pathLower.Contains("benchmark"))
            return true;

        try
        {
            var content = File.ReadAllText(csprojPath);
            return content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
                || content.Contains("xunit", StringComparison.OrdinalIgnoreCase)
                || content.Contains("nunit", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
