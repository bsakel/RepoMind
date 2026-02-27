using System.Text.Json;
using System.Text.Json.Serialization;
using RepoMind.Scanner.Models;
using RepoMind.Scanner.Parsers;
using Serilog;

namespace RepoMind.Scanner.Writers;

public static class FlatFileWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void WriteAll(
        string outputDir,
        List<ProjectInfo> projects,
        Dictionary<string, List<AssemblyInfo>> assembliesByProject,
        Dictionary<string, List<TypeInfo>> typesByProject,
        Dictionary<string, List<ConfigEntry>>? configByProject = null)
    {
        Directory.CreateDirectory(outputDir);

        WriteProjectsCatalog(outputDir, projects, assembliesByProject);
        WriteDependencyGraph(outputDir, assembliesByProject);
        WriteTypesIndex(outputDir, typesByProject);
        WritePerProjectSummaries(outputDir, projects, assembliesByProject, typesByProject, configByProject);

        Log.Information("Flat files written to {OutputDir}", outputDir);
    }

    private static void WriteProjectsCatalog(
        string outputDir, List<ProjectInfo> projects,
        Dictionary<string, List<AssemblyInfo>> assembliesByProject)
    {
        var catalog = projects.Select(p => new
        {
            p.Name,
            p.SolutionFile,
            Assemblies = assembliesByProject.GetValueOrDefault(p.Name, [])
                .Select(a => new { a.AssemblyName, a.TargetFramework, a.OutputType })
        });

        var json = JsonSerializer.Serialize(catalog, JsonOpts);
        File.WriteAllText(Path.Combine(outputDir, "projects.json"), json);
    }

    private static void WriteDependencyGraph(
        string outputDir, Dictionary<string, List<AssemblyInfo>> assembliesByProject)
    {
        var graph = new Dictionary<string, List<string>>();
        foreach (var (project, assemblies) in assembliesByProject)
        {
            var deps = assemblies
                .SelectMany(a => a.PackageReferences)
                .Where(p => p.IsInternal)
                .Select(p => p.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            if (deps.Count > 0) graph[project] = deps;
        }

        var json = JsonSerializer.Serialize(graph, JsonOpts);
        File.WriteAllText(Path.Combine(outputDir, "dependency-graph.json"), json);
    }

    private static void WriteTypesIndex(
        string outputDir, Dictionary<string, List<TypeInfo>> typesByProject)
    {
        var publicTypes = typesByProject
            .SelectMany(kvp => kvp.Value.Where(t => t.IsPublic).Select(t => new
            {
                Project = kvp.Key,
                t.NamespaceName,
                t.TypeName,
                t.Kind,
                t.BaseType,
                Interfaces = t.ImplementedInterfaces.Count > 0 ? t.ImplementedInterfaces : null,
                InjectedDeps = t.InjectedDependencies.Count > 0 ? t.InjectedDependencies : null,
                Methods = t.Methods?.Where(m => m.IsPublic).Select(m => new
                {
                    m.MethodName,
                    m.ReturnType,
                    m.IsStatic,
                    Parameters = m.Parameters.Count > 0 ? m.Parameters.Select(p => $"{p.Type} {p.Name}").ToList() : null,
                    Endpoints = m.Endpoints.Count > 0 ? m.Endpoints.Select(e => $"[{e.HttpMethod}] {e.RouteTemplate}").ToList() : null
                }).ToList() is { Count: > 0 } methods ? methods : null
            }))
            .OrderBy(t => t.NamespaceName)
            .ThenBy(t => t.TypeName);

        var json = JsonSerializer.Serialize(publicTypes, JsonOpts);
        File.WriteAllText(Path.Combine(outputDir, "types-index.json"), json);
    }

    private static void WritePerProjectSummaries(
        string outputDir, List<ProjectInfo> projects,
        Dictionary<string, List<AssemblyInfo>> assembliesByProject,
        Dictionary<string, List<TypeInfo>> typesByProject,
        Dictionary<string, List<ConfigEntry>>? configByProject)
    {
        var projectDir = Path.Combine(outputDir, "projects");
        Directory.CreateDirectory(projectDir);

        foreach (var project in projects)
        {
            var assemblies = assembliesByProject.GetValueOrDefault(project.Name, []);
            var types = typesByProject.GetValueOrDefault(project.Name, []);
            var config = configByProject?.GetValueOrDefault(project.Name, []) ?? [];

            var lines = new List<string>
            {
                $"# {project.Name}",
                "",
                $"**Solution:** {project.SolutionFile ?? "N/A"}",
                $"**Path:** {project.DirectoryPath}",
                "",
                "## Assemblies",
                ""
            };

            foreach (var asm in assemblies)
            {
                lines.Add($"### {asm.AssemblyName}");
                lines.Add($"- **Framework:** {asm.TargetFramework ?? "N/A"}");
                lines.Add($"- **Output:** {asm.OutputType ?? "Library"}");
                if (asm.PackageReferences.Count > 0)
                {
                    lines.Add("- **NuGet Dependencies:**");
                    foreach (var pkg in asm.PackageReferences.OrderBy(p => p.Name))
                        lines.Add($"  - {pkg.Name} {pkg.Version}{(pkg.IsInternal ? " *(internal)*" : "")}");
                }
                if (asm.ProjectReferences.Count > 0)
                {
                    lines.Add("- **Project References:**");
                    foreach (var pr in asm.ProjectReferences)
                        lines.Add($"  - {pr}");
                }
                lines.Add("");
            }

            if (types.Count > 0)
            {
                var publicTypes = types.Where(t => t.IsPublic).ToList();
                lines.Add("## Public Types");
                lines.Add("");

                var byNs = publicTypes.GroupBy(t => t.NamespaceName).OrderBy(g => g.Key);
                foreach (var ns in byNs)
                {
                    lines.Add($"### {ns.Key}");
                    foreach (var t in ns.OrderBy(x => x.TypeName))
                    {
                        var extras = new List<string>();
                        if (t.BaseType != null) extras.Add($"extends {t.BaseType}");
                        if (t.ImplementedInterfaces.Count > 0) extras.Add($"implements {string.Join(", ", t.ImplementedInterfaces)}");
                        var suffix = extras.Count > 0 ? $" — {string.Join("; ", extras)}" : "";
                        lines.Add($"- `{t.Kind}` **{t.TypeName}**{suffix}");

                        // Methods
                        if (t.Methods != null)
                        {
                            foreach (var m in t.Methods.Where(m => m.IsPublic))
                            {
                                var parms = string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"));
                                lines.Add($"  - `{m.ReturnType}` {m.MethodName}({parms})");
                                foreach (var ep in m.Endpoints)
                                    lines.Add($"    - **{ep.Kind}:** `[{ep.HttpMethod}] {ep.RouteTemplate}`");
                            }
                        }
                    }
                    lines.Add("");
                }
            }

            // Configuration keys
            if (config.Count > 0)
            {
                lines.Add("## Configuration");
                lines.Add("");
                var bySource = config.GroupBy(c => c.Source).OrderBy(g => g.Key);
                foreach (var group in bySource)
                {
                    lines.Add($"### {group.Key}");
                    foreach (var entry in group.OrderBy(e => e.KeyName))
                    {
                        var defaultPart = entry.DefaultValue != null ? $" (default: `{entry.DefaultValue}`)" : "";
                        lines.Add($"- `{entry.KeyName}`{defaultPart}");
                    }
                    lines.Add("");
                }
            }

            File.WriteAllText(Path.Combine(projectDir, $"{project.Name}.md"), string.Join('\n', lines));
        }
    }
}
