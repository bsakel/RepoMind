using System.Text;
using RepoMind.Mcp.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace RepoMind.Mcp.Services;

public class QueryService
{
    private readonly RepoMindConfiguration _config;
    private readonly ILogger<QueryService> _logger;
    private readonly Func<SqliteConnection>? _connectionFactory;

    public QueryService(RepoMindConfiguration config, ILogger<QueryService> logger, Func<SqliteConnection>? connectionFactory = null)
    {
        _config = config;
        _logger = logger;
        _connectionFactory = connectionFactory;
    }

    private const string NoDatabaseMessage =
        "⚠️ Memory database not found. Run the `rescan_memory` tool first to scan your codebase.";

    internal const string CreateTypeWithProjectView = @"
        CREATE TEMP VIEW IF NOT EXISTS type_with_project AS
        SELECT t.*, n.namespace_name, a.assembly_name, a.target_framework,
               a.is_test, p.name AS project_name, p.git_remote_url
        FROM types t
        JOIN namespaces n ON t.namespace_id = n.id
        JOIN assemblies a ON n.assembly_id = a.id
        JOIN projects p ON a.project_id = p.id";

    private SqliteConnection OpenConnection()
    {
        if (!File.Exists(_config.DbPath))
        {
            _logger.LogWarning("Memory database not found at {DbPath}", _config.DbPath);
            throw new DatabaseNotFoundException(NoDatabaseMessage);
        }

        _logger.LogDebug("Opening database connection to {DbPath}", _config.DbPath);
        var conn = new SqliteConnection($"Data Source={_config.DbPath};Mode=ReadOnly");
        conn.Open();
        return conn;
    }

    private SqliteConnection GetConnection()
    {
        var conn = _connectionFactory?.Invoke() ?? OpenConnection();
        EnsureViews(conn);
        return conn;
    }

    private void EnsureViews(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = CreateTypeWithProjectView;
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8)
        {
            // SQLITE_READONLY — view already created (e.g., test fixture)
        }
    }

    private bool OwnsConnection => _connectionFactory == null;

    private void MaybeClose(SqliteConnection conn)
    {
        if (OwnsConnection) conn.Dispose();
    }

    public string ListProjects()
    {
        var conn = GetConnection();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT p.name, p.git_remote_url,
                       COUNT(DISTINCT a.id) as assembly_count,
                       COUNT(DISTINCT t.id) as type_count
                FROM projects p
                LEFT JOIN assemblies a ON a.project_id = p.id AND a.is_test = 0
                LEFT JOIN namespaces n ON n.assembly_id = a.id
                LEFT JOIN types t ON t.namespace_id = n.id
                GROUP BY p.id
                ORDER BY p.name";

            using var reader = cmd.ExecuteReader();
            var lines = new List<string> { "| Project | Assemblies | Types |", "| --- | --- | --- |" };
            while (reader.Read())
            {
                lines.Add($"| {reader.GetString(0)} | {reader.GetInt32(2)} | {reader.GetInt32(3)} |");
            }
            return BuildMarkdownTable(lines, "No projects found.");
        }
        finally { MaybeClose(conn); }
    }

    public string GetProjectInfo(string projectName)
    {
        var conn = GetConnection();
        try
        {
            var resolvedName = ResolveProjectName(conn, projectName);
            if (resolvedName == null)
                return $"Project '{projectName}' not found.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {resolvedName}");

            // Basic info
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT directory_path, solution_file, git_remote_url FROM projects WHERE name = @name";
                cmd.Parameters.AddWithValue("@name", resolvedName);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    sb.AppendLine($"- **Path:** {reader.GetString(0)}");
                    if (!reader.IsDBNull(1)) sb.AppendLine($"- **Solution:** {reader.GetString(1)}");
                    if (!reader.IsDBNull(2)) sb.AppendLine($"- **Remote:** {reader.GetString(2)}");
                }
            }

            // Assemblies
            sb.AppendLine("\n## Assemblies");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT a.assembly_name, a.target_framework, a.is_test
                    FROM assemblies a JOIN projects p ON a.project_id = p.id
                    WHERE p.name = @name ORDER BY a.is_test, a.assembly_name";
                cmd.Parameters.AddWithValue("@name", resolvedName);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var testTag = reader.GetInt32(2) == 1 ? " (test)" : "";
                    sb.AppendLine($"- {reader.GetString(0)} [{reader.GetString(1)}]{testTag}");
                }
            }

            // Internal dependencies
            sb.AppendLine("\n## Internal Dependencies (NuGet)");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT DISTINCT pr.package_name, pr.version
                    FROM package_references pr
                    JOIN assemblies a ON pr.assembly_id = a.id
                    JOIN projects p ON a.project_id = p.id
                    WHERE p.name = @name AND pr.is_internal = 1
                    ORDER BY pr.package_name";
                cmd.Parameters.AddWithValue("@name", resolvedName);
                using var reader = cmd.ExecuteReader();
                var found = false;
                while (reader.Read())
                {
                    found = true;
                    sb.AppendLine($"- {reader.GetString(0)} {reader.GetString(1)}");
                }
                if (!found) sb.AppendLine("None");
            }

            // External dependencies (top 15)
            sb.AppendLine("\n## External Dependencies (top 15)");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT DISTINCT pr.package_name, pr.version
                    FROM package_references pr
                    JOIN assemblies a ON pr.assembly_id = a.id
                    JOIN projects p ON a.project_id = p.id
                    WHERE p.name = @name AND pr.is_internal = 0
                    ORDER BY pr.package_name
                    LIMIT 15";
                cmd.Parameters.AddWithValue("@name", resolvedName);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var version = reader.IsDBNull(1) ? "" : $" {reader.GetString(1)}";
                    sb.AppendLine($"- {reader.GetString(0)}{version}");
                }
            }

            // Namespaces and type counts
            sb.AppendLine("\n## Namespaces");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT n.namespace_name, COUNT(t.id) as type_count
                    FROM namespaces n
                    JOIN assemblies a ON n.assembly_id = a.id
                    JOIN projects p ON a.project_id = p.id
                    LEFT JOIN types t ON t.namespace_id = n.id
                    WHERE p.name = @name
                    GROUP BY n.id
                    ORDER BY n.namespace_name";
                cmd.Parameters.AddWithValue("@name", resolvedName);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    sb.AppendLine($"- {reader.GetString(0)} ({reader.GetInt32(1)} types)");
                }
            }

            // Key public types (up to 30)
            sb.AppendLine("\n## Key Public Types (up to 30)");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT type_name, kind, namespace_name
                    FROM type_with_project
                    WHERE project_name = @name AND is_public = 1
                    ORDER BY kind, type_name
                    LIMIT 30";
                cmd.Parameters.AddWithValue("@name", resolvedName);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    sb.AppendLine($"- `{reader.GetString(0)}` ({reader.GetString(1)}) in {reader.GetString(2)}");
                }
            }

            return sb.ToString();
        }
        finally { MaybeClose(conn); }
    }

    public string GetDependencyGraph(string projectName)
    {
        var conn = GetConnection();
        try
        {
            var resolvedName = ResolveProjectName(conn, projectName);
            if (resolvedName == null)
                return $"Project '{projectName}' not found.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Dependency Graph: {resolvedName}");

            // Upstream: what this project depends on
            sb.AppendLine("\n## Depends On (upstream)");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT DISTINCT pr.package_name, pr.version
                    FROM package_references pr
                    JOIN assemblies a ON pr.assembly_id = a.id
                    JOIN projects p ON a.project_id = p.id
                    WHERE p.name = @name AND pr.is_internal = 1
                    ORDER BY pr.package_name";
                cmd.Parameters.AddWithValue("@name", resolvedName);
                using var reader = cmd.ExecuteReader();
                var found = false;
                while (reader.Read())
                {
                    found = true;
                    sb.AppendLine($"- {reader.GetString(0)} {reader.GetString(1)}");
                }
                if (!found) sb.AppendLine("None (foundation library)");
            }

            // Downstream: what depends on this project
            sb.AppendLine("\n## Depended On By (downstream)");
            using (var cmd = conn.CreateCommand())
            {
                // Find projects that have internal package references matching this project's assembly names
                cmd.CommandText = @"
                    SELECT DISTINCT p2.name
                    FROM package_references pr
                    JOIN assemblies a ON pr.assembly_id = a.id
                    JOIN projects p2 ON a.project_id = p2.id
                    WHERE pr.is_internal = 1
                      AND pr.package_name IN (
                          SELECT a2.assembly_name FROM assemblies a2
                          JOIN projects p3 ON a2.project_id = p3.id
                          WHERE p3.name = @name
                      )
                      AND p2.name != @name
                    ORDER BY p2.name";
                cmd.Parameters.AddWithValue("@name", resolvedName);
                using var reader = cmd.ExecuteReader();
                var found = false;
                while (reader.Read())
                {
                    found = true;
                    sb.AppendLine($"- {reader.GetString(0)}");
                }
                if (!found) sb.AppendLine("None (leaf service)");
            }

            return sb.ToString();
        }
        finally { MaybeClose(conn); }
    }

    public string SearchTypes(string namePattern, string? namespaceName = null, string? kind = null, string? projectName = null)
    {
        _logger.LogDebug("Executing SearchTypes with pattern: {Pattern}", namePattern);
        var conn = GetConnection();
        try
        {
            // Convert * wildcards to SQL LIKE %
            var sqlPattern = ToSqlPattern(namePattern);

            using var cmd = conn.CreateCommand();
            var where = new List<string> { "type_name LIKE @pattern" };
            cmd.Parameters.AddWithValue("@pattern", sqlPattern);

            if (!string.IsNullOrEmpty(namespaceName))
            {
                where.Add("namespace_name LIKE @ns");
                cmd.Parameters.AddWithValue("@ns", ToSqlPattern(namespaceName));
            }
            if (!string.IsNullOrEmpty(kind))
            {
                where.Add("kind = @kind");
                cmd.Parameters.AddWithValue("@kind", kind.ToLowerInvariant());
            }
            if (!string.IsNullOrEmpty(projectName))
            {
                where.Add("project_name LIKE @proj");
                cmd.Parameters.AddWithValue("@proj", $"%{projectName}%");
            }

            cmd.CommandText = $@"
                SELECT type_name, kind, namespace_name, project_name, file_path
                FROM type_with_project
                WHERE {string.Join(" AND ", where)}
                ORDER BY project_name, namespace_name, type_name
                LIMIT 50";

            using var reader = cmd.ExecuteReader();
            var lines = new List<string> { "| Type | Kind | Namespace | Project | File |", "| --- | --- | --- | --- | --- |" };
            while (reader.Read())
            {
                var file = reader.IsDBNull(4) ? "" : reader.GetString(4);
                lines.Add($"| {reader.GetString(0)} | {reader.GetString(1)} | {reader.GetString(2)} | {reader.GetString(3)} | {file} |");
            }
            return BuildMarkdownTable(lines, $"No types matching '{namePattern}'.");
        }
        finally { MaybeClose(conn); }
    }

    public string FindImplementors(string interfaceName)
    {
        _logger.LogDebug("Executing FindImplementors with interface: {InterfaceName}", interfaceName);
        var conn = GetConnection();
        try
        {
            var sqlPattern = ToSqlPattern(interfaceName);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT v.type_name, v.kind, v.namespace_name, v.project_name, v.file_path, ti.interface_name
                FROM type_with_project v
                JOIN type_interfaces ti ON ti.type_id = v.id
                WHERE ti.interface_name LIKE @iface
                ORDER BY v.project_name, v.type_name
                LIMIT 100";
            cmd.Parameters.AddWithValue("@iface", sqlPattern);

            using var reader = cmd.ExecuteReader();
            var lines = new List<string> { "| Implementing Type | Kind | Interface | Project | File |", "| --- | --- | --- | --- | --- |" };
            while (reader.Read())
            {
                var file = reader.IsDBNull(4) ? "" : reader.GetString(4);
                lines.Add($"| {reader.GetString(0)} | {reader.GetString(1)} | {reader.GetString(5)} | {reader.GetString(3)} | {file} |");
            }
            return BuildMarkdownTable(lines, $"No implementors of '{interfaceName}' found.");
        }
        finally { MaybeClose(conn); }
    }

    public string GetTypeDetails(string typeName)
    {
        _logger.LogDebug("Executing GetTypeDetails for type: {TypeName}", typeName);
        var conn = GetConnection();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, type_name, kind, is_public, file_path, base_type, summary_comment,
                       namespace_name, project_name
                FROM type_with_project
                WHERE type_name = @name
                LIMIT 5";
            cmd.Parameters.AddWithValue("@name", typeName);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return $"Type '{typeName}' not found.";

            var sb = new System.Text.StringBuilder();
            // Could be multiple types with same name in different namespaces
            do
            {
                var typeId = reader.GetInt32(0);
                sb.AppendLine($"# {reader.GetString(1)}");
                sb.AppendLine($"- **Kind:** {reader.GetString(2)}");
                sb.AppendLine($"- **Public:** {(reader.GetInt32(3) == 1 ? "Yes" : "No")}");
                sb.AppendLine($"- **Namespace:** {reader.GetString(7)}");
                sb.AppendLine($"- **Project:** {reader.GetString(8)}");
                if (!reader.IsDBNull(4)) sb.AppendLine($"- **File:** {reader.GetString(4)}");
                if (!reader.IsDBNull(5)) sb.AppendLine($"- **Base Type:** {reader.GetString(5)}");
                if (!reader.IsDBNull(6)) sb.AppendLine($"- **Summary:** {reader.GetString(6)}");

                // Interfaces
                using (var ifCmd = conn.CreateCommand())
                {
                    ifCmd.CommandText = "SELECT interface_name FROM type_interfaces WHERE type_id = @id";
                    ifCmd.Parameters.AddWithValue("@id", typeId);
                    using var ifReader = ifCmd.ExecuteReader();
                    var interfaces = new List<string>();
                    while (ifReader.Read()) interfaces.Add(ifReader.GetString(0));
                    if (interfaces.Count > 0)
                        sb.AppendLine($"- **Implements:** {string.Join(", ", interfaces)}");
                }

                // Injected dependencies
                using (var depCmd = conn.CreateCommand())
                {
                    depCmd.CommandText = "SELECT dependency_type FROM type_injected_deps WHERE type_id = @id";
                    depCmd.Parameters.AddWithValue("@id", typeId);
                    using var depReader = depCmd.ExecuteReader();
                    var deps = new List<string>();
                    while (depReader.Read()) deps.Add(depReader.GetString(0));
                    if (deps.Count > 0)
                        sb.AppendLine($"- **Injected Dependencies:** {string.Join(", ", deps)}");
                }

                sb.AppendLine();
            } while (reader.Read());

            return sb.ToString();
        }
        finally { MaybeClose(conn); }
    }

    public string SearchInjections(string dependencyName)
    {
        var conn = GetConnection();
        try
        {
            var sqlPattern = ToSqlPattern(dependencyName);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT v.type_name, v.namespace_name, v.project_name, v.file_path, tid.dependency_type
                FROM type_injected_deps tid
                JOIN type_with_project v ON tid.type_id = v.id
                WHERE tid.dependency_type LIKE @dep
                ORDER BY v.project_name, v.type_name
                LIMIT 100";
            cmd.Parameters.AddWithValue("@dep", sqlPattern);

            using var reader = cmd.ExecuteReader();
            var lines = new List<string> { "| Type | Dependency | Project | File |", "| --- | --- | --- | --- |" };
            while (reader.Read())
            {
                var file = reader.IsDBNull(3) ? "" : reader.GetString(3);
                lines.Add($"| {reader.GetString(0)} | {reader.GetString(4)} | {reader.GetString(2)} | {file} |");
            }
            return BuildMarkdownTable(lines, $"No types inject '{dependencyName}'.");
        }
        finally { MaybeClose(conn); }
    }

    public string GetPackageVersions(string packageName)
    {
        var conn = GetConnection();
        try
        {
            var sqlPattern = ToSqlPattern(packageName);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT p.name, pr.package_name, pr.version
                FROM package_references pr
                JOIN assemblies a ON pr.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id
                WHERE pr.package_name LIKE @pkg
                ORDER BY pr.package_name, p.name";
            cmd.Parameters.AddWithValue("@pkg", sqlPattern);

            using var reader = cmd.ExecuteReader();
            var lines = new List<string> { "| Package | Version | Project |", "| --- | --- | --- |" };
            var versions = new Dictionary<string, HashSet<string>>();
            while (reader.Read())
            {
                var pkg = reader.GetString(1);
                var ver = reader.IsDBNull(2) ? "unspecified" : reader.GetString(2);
                lines.Add($"| {pkg} | {ver} | {reader.GetString(0)} |");

                if (!versions.ContainsKey(pkg)) versions[pkg] = new HashSet<string>();
                versions[pkg].Add(ver);
            }

            if (lines.Count == 2)
                return $"No packages matching '{packageName}'.";

            // Check for version mismatches
            var mismatches = versions.Where(kv => kv.Value.Count > 1).ToList();
            if (mismatches.Count > 0)
            {
                lines.Add("");
                lines.Add("⚠️ **Version mismatches detected:**");
                foreach (var m in mismatches)
                    lines.Add($"- {m.Key}: {string.Join(", ", m.Value)}");
            }

            return string.Join("\n", lines);
        }
        finally { MaybeClose(conn); }
    }

    private static string? ResolveProjectName(SqliteConnection conn, string input)
    {
        // Try exact match first
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM projects WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", input);
        var result = cmd.ExecuteScalar() as string;
        if (result != null) return result;

        // Try partial match (e.g. "caching" -> "acme.caching")
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT name FROM projects WHERE name LIKE @pattern ORDER BY name LIMIT 1";
        cmd2.Parameters.AddWithValue("@pattern", $"%{input}%");
        return cmd2.ExecuteScalar() as string;
    }

    public string SearchEndpoints(string routePattern)
    {
        _logger.LogDebug("Executing SearchEndpoints with pattern: {RoutePattern}", routePattern);
        var conn = GetConnection();
        try
        {
            var sqlPattern = ToSqlPattern(routePattern);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT e.http_method, e.route_template, e.endpoint_kind,
                       m.method_name, v.type_name, v.namespace_name, v.project_name, v.file_path
                FROM endpoints e
                JOIN methods m ON e.method_id = m.id
                JOIN type_with_project v ON e.type_id = v.id
                WHERE e.route_template LIKE @pattern
                   OR m.method_name LIKE @pattern
                ORDER BY e.endpoint_kind, e.http_method, e.route_template
                LIMIT 100";
            cmd.Parameters.AddWithValue("@pattern", sqlPattern);

            using var reader = cmd.ExecuteReader();
            var lines = new List<string>
            {
                "| Method | Route | Kind | Handler | Type | Project |",
                "| --- | --- | --- | --- | --- | --- |"
            };
            while (reader.Read())
            {
                var route = reader.IsDBNull(1) ? "" : reader.GetString(1);
                lines.Add($"| {reader.GetString(0)} | {route} | {reader.GetString(2)} | {reader.GetString(3)} | {reader.GetString(4)} | {reader.GetString(6)} |");
            }
            return BuildMarkdownTable(lines, $"No endpoints matching '{routePattern}'.");
        }
        finally { MaybeClose(conn); }
    }

    public string SearchMethods(string namePattern, string? returnType = null, string? projectName = null)
    {
        _logger.LogDebug("Executing SearchMethods with pattern: {Pattern}", namePattern);
        var conn = GetConnection();
        try
        {
            var sqlPattern = ToSqlPattern(namePattern);

            using var cmd = conn.CreateCommand();
            var where = new List<string> { "m.method_name LIKE @pattern" };
            cmd.Parameters.AddWithValue("@pattern", sqlPattern);

            if (!string.IsNullOrEmpty(returnType))
            {
                where.Add("m.return_type LIKE @ret");
                cmd.Parameters.AddWithValue("@ret", $"%{returnType}%");
            }
            if (!string.IsNullOrEmpty(projectName))
            {
                where.Add("v.project_name LIKE @proj");
                cmd.Parameters.AddWithValue("@proj", $"%{projectName}%");
            }

            cmd.CommandText = $@"
                SELECT m.method_name, m.return_type, v.type_name, v.namespace_name, v.project_name, v.file_path
                FROM methods m
                JOIN type_with_project v ON m.type_id = v.id
                WHERE {string.Join(" AND ", where)}
                ORDER BY v.project_name, v.type_name, m.method_name
                LIMIT 100";

            using var reader = cmd.ExecuteReader();
            var lines = new List<string>
            {
                "| Method | Returns | Type | Project | File |",
                "| --- | --- | --- | --- | --- |"
            };
            while (reader.Read())
            {
                var file = reader.IsDBNull(5) ? "" : reader.GetString(5);
                lines.Add($"| {reader.GetString(0)} | {reader.GetString(1)} | {reader.GetString(2)} | {reader.GetString(4)} | {file} |");
            }
            return BuildMarkdownTable(lines, $"No methods matching '{namePattern}'.");
        }
        finally { MaybeClose(conn); }
    }

    public string GenerateAgentsMd(string? productName = null)
    {
        var conn = GetConnection();
        try
        {
            var sb = new StringBuilder();

            // Gather stats
            var projectCount = ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM projects");
            var typeCount = ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM types WHERE is_public = 1");
            var assemblyCount = ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM assemblies WHERE is_test = 0");
            var endpointCount = ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM endpoints");

            var name = productName ?? "This Codebase";

            // Header
            sb.AppendLine($"# {name} — Agent Instructions");
            sb.AppendLine();
            sb.AppendLine("## Overview");
            sb.AppendLine();
            sb.AppendLine($"This codebase consists of **{projectCount} projects** with **{assemblyCount} assemblies**, " +
                $"**{typeCount} public types**, and **{endpointCount} endpoints**.");
            sb.AppendLine();

            // Projects list
            sb.AppendLine("## Projects");
            sb.AppendLine();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT p.name, p.git_remote_url,
                        (SELECT COUNT(*) FROM assemblies a WHERE a.project_id = p.id AND a.is_test = 0) as asm_count,
                        (SELECT COUNT(*) FROM types t
                         JOIN namespaces n ON t.namespace_id = n.id
                         JOIN assemblies a ON n.assembly_id = a.id
                         WHERE a.project_id = p.id AND t.is_public = 1) as type_count
                    FROM projects p ORDER BY p.name";
                using var reader = cmd.ExecuteReader();
                sb.AppendLine("| Project | Assemblies | Public Types |");
                sb.AppendLine("| --- | --- | --- |");
                while (reader.Read())
                {
                    sb.AppendLine($"| {reader.GetString(0)} | {reader.GetInt64(2)} | {reader.GetInt64(3)} |");
                }
            }
            sb.AppendLine();

            // Dependency graph
            sb.AppendLine("## Internal Dependencies");
            sb.AppendLine();
            sb.AppendLine("Projects that consume other projects via internal packages:");
            sb.AppendLine();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT DISTINCT p.name as consumer, pr.package_name as dependency
                    FROM package_references pr
                    JOIN assemblies a ON pr.assembly_id = a.id
                    JOIN projects p ON a.project_id = p.id
                    WHERE pr.is_internal = 1
                    ORDER BY p.name, pr.package_name";
                using var reader = cmd.ExecuteReader();
                var current = "";
                while (reader.Read())
                {
                    var project = reader.GetString(0);
                    var dep = reader.GetString(1);
                    if (project != current)
                    {
                        if (current != "") sb.AppendLine();
                        sb.Append($"- **{project}** → {dep}");
                        current = project;
                    }
                    else
                    {
                        sb.Append($", {dep}");
                    }
                }
                if (current != "") sb.AppendLine();
            }
            sb.AppendLine();

            // Conventions (auto-detected)
            sb.AppendLine("## Detected Conventions");
            sb.AppendLine();

            // Target frameworks
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT target_framework, COUNT(*) as cnt
                    FROM assemblies WHERE is_test = 0 AND target_framework IS NOT NULL
                    GROUP BY target_framework ORDER BY cnt DESC";
                using var reader = cmd.ExecuteReader();
                sb.Append("- **Target Framework:** ");
                var frameworks = new List<string>();
                while (reader.Read())
                    frameworks.Add($"{reader.GetString(0)} ({reader.GetInt64(1)} projects)");
                sb.AppendLine(frameworks.Count > 0 ? string.Join(", ", frameworks) : "unknown");
            }

            // Common external packages
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT package_name, COUNT(DISTINCT a.project_id) as project_count
                    FROM package_references pr
                    JOIN assemblies a ON pr.assembly_id = a.id
                    WHERE pr.is_internal = 0 AND a.is_test = 0
                    GROUP BY package_name
                    HAVING project_count >= 3
                    ORDER BY project_count DESC
                    LIMIT 15";
                using var reader = cmd.ExecuteReader();
                sb.AppendLine("- **Common Packages:**");
                while (reader.Read())
                    sb.AppendLine($"  - {reader.GetString(0)} (used by {reader.GetInt64(1)} projects)");
            }

            // Test framework detection
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT package_name, COUNT(*) as cnt
                    FROM package_references pr
                    JOIN assemblies a ON pr.assembly_id = a.id
                    WHERE a.is_test = 1
                    AND (package_name LIKE '%xunit%' OR package_name LIKE '%nunit%' OR package_name LIKE '%mstest%'
                         OR package_name LIKE '%Moq%' OR package_name LIKE '%NSubstitute%' OR package_name LIKE '%FluentAssertions%')
                    GROUP BY package_name ORDER BY cnt DESC";
                using var reader = cmd.ExecuteReader();
                var testPkgs = new List<string>();
                while (reader.Read()) testPkgs.Add(reader.GetString(0));
                if (testPkgs.Count > 0)
                    sb.AppendLine($"- **Testing:** {string.Join(", ", testPkgs)}");
            }
            sb.AppendLine();

            // Endpoints summary
            if (endpointCount > 0)
            {
                sb.AppendLine("## API Endpoints");
                sb.AppendLine();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT e.http_method, e.route_template, e.endpoint_kind, v.type_name, v.project_name
                    FROM endpoints e
                    JOIN methods m ON e.method_id = m.id
                    JOIN type_with_project v ON e.type_id = v.id
                    ORDER BY e.endpoint_kind, e.http_method, e.route_template
                    LIMIT 50";
                using var reader = cmd.ExecuteReader();
                sb.AppendLine("| Method | Route | Kind | Type | Project |");
                sb.AppendLine("| --- | --- | --- | --- | --- |");
                while (reader.Read())
                {
                    var route = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    sb.AppendLine($"| {reader.GetString(0)} | {route} | {reader.GetString(2)} | {reader.GetString(3)} | {reader.GetString(4)} |");
                }
                sb.AppendLine();
            }

            // Key interfaces
            sb.AppendLine("## Key Interfaces");
            sb.AppendLine();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT type_name, namespace_name, project_name,
                        (SELECT COUNT(*) FROM type_interfaces ti WHERE ti.interface_name = type_name) as impl_count
                    FROM type_with_project
                    WHERE kind = 'interface' AND is_public = 1
                    ORDER BY impl_count DESC, type_name
                    LIMIT 20";
                using var reader = cmd.ExecuteReader();
                sb.AppendLine("| Interface | Namespace | Project | Implementors |");
                sb.AppendLine("| --- | --- | --- | --- |");
                while (reader.Read())
                {
                    sb.AppendLine($"| {reader.GetString(0)} | {reader.GetString(1)} | {reader.GetString(2)} | {reader.GetInt64(3)} |");
                }
            }
            sb.AppendLine();

            // Footer
            sb.AppendLine("---");
            sb.AppendLine($"*Generated by RepoMind on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC*");

            return sb.ToString();
        }
        finally { MaybeClose(conn); }
    }

    private static T ExecuteScalar<T>(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = cmd.ExecuteScalar();
        return result is T typed ? typed : throw new InvalidOperationException($"Query returned null or unexpected type");
    }

    private static string ToSqlPattern(string input)
    {
        var pattern = input.Replace("*", "%");
        if (!pattern.Contains('%')) pattern = $"%{pattern}%";
        return pattern;
    }

    private static string BuildMarkdownTable(List<string> lines, string emptyMessage)
    {
        return lines.Count == 2 ? emptyMessage : string.Join("\n", lines);
    }

    public string SearchConfig(string keyPattern, string? source = null, string? projectName = null)
    {
        var conn = GetConnection();
        try
        {
            var sqlPattern = ToSqlPattern(keyPattern);

            using var cmd = conn.CreateCommand();
            var where = new List<string> { "key_name LIKE @pattern" };
            cmd.Parameters.AddWithValue("@pattern", sqlPattern);

            if (!string.IsNullOrEmpty(source))
            {
                where.Add("source = @src");
                cmd.Parameters.AddWithValue("@src", source);
            }
            if (!string.IsNullOrEmpty(projectName))
            {
                where.Add("project_name LIKE @proj");
                cmd.Parameters.AddWithValue("@proj", $"%{projectName}%");
            }

            cmd.CommandText = $@"
                SELECT project_name, source, key_name, default_value, file_path
                FROM config_keys
                WHERE {string.Join(" AND ", where)}
                ORDER BY key_name, project_name
                LIMIT 100";

            using var reader = cmd.ExecuteReader();
            var lines = new List<string>
            {
                "| Project | Source | Key | Default | File |",
                "| --- | --- | --- | --- | --- |"
            };
            while (reader.Read())
            {
                var defaultVal = reader.IsDBNull(3) ? "" : reader.GetString(3);
                lines.Add($"| {reader.GetString(0)} | {reader.GetString(1)} | {reader.GetString(2)} | {defaultVal} | {reader.GetString(4)} |");
            }
            return BuildMarkdownTable(lines, $"No config keys matching '{keyPattern}'.");
        }
        finally { MaybeClose(conn); }
    }

    public string TraceFlow(string typeName, int maxDepth = 3)
    {
        _logger.LogDebug("Executing TraceFlow for type: {TypeName}, maxDepth: {MaxDepth}", typeName, maxDepth);
        var conn = GetConnection();
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Flow Trace: {typeName}");
            sb.AppendLine();

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TraceFlowRecursive(conn, sb, typeName, 0, maxDepth, visited);

            if (visited.Count <= 1)
                sb.AppendLine("No flow connections found for this type.");

            return sb.ToString();
        }
        finally { MaybeClose(conn); }
    }

    private void TraceFlowRecursive(SqliteConnection conn, StringBuilder sb, string typeName, int depth, int maxDepth, HashSet<string> visited)
    {
        if (depth > maxDepth || !visited.Add(typeName)) return;

        var indent = new string(' ', depth * 2);

        // Find implementors of this type (if it's an interface)
        var implementors = QueryTypeList(conn,
            "SELECT DISTINCT v.type_name, v.project_name FROM type_with_project v " +
            "JOIN type_interfaces ti ON ti.type_id = v.id " +
            "WHERE ti.interface_name = @name", typeName);

        if (implementors.Count > 0)
        {
            sb.AppendLine($"{indent}**{typeName}** implemented by:");
            foreach (var (implType, project) in implementors)
            {
                sb.AppendLine($"{indent}  → {implType} ({project})");
                // Find who injects this implementor
                TraceInjectors(conn, sb, implType, depth + 1, maxDepth, visited);
            }
        }

        // Find types that inject this type directly
        TraceInjectors(conn, sb, typeName, depth, maxDepth, visited);
    }

    private void TraceInjectors(SqliteConnection conn, StringBuilder sb, string typeName, int depth, int maxDepth, HashSet<string> visited)
    {
        var indent = new string(' ', depth * 2);

        var injectors = QueryTypeList(conn,
            "SELECT DISTINCT v.type_name, v.project_name FROM type_with_project v " +
            "JOIN type_injected_deps tid ON tid.type_id = v.id " +
            "WHERE tid.dependency_type = @name", typeName);

        if (injectors.Count > 0)
        {
            sb.AppendLine($"{indent}**{typeName}** injected into:");
            foreach (var (injType, project) in injectors)
            {
                sb.AppendLine($"{indent}  ← {injType} ({project})");
                if (depth + 1 <= maxDepth)
                    TraceFlowRecursive(conn, sb, injType, depth + 1, maxDepth, visited);
            }
        }
    }

    private static List<(string TypeName, string Project)> QueryTypeList(SqliteConnection conn, string sql, string typeName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@name", typeName);
        using var reader = cmd.ExecuteReader();
        var results = new List<(string, string)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1)));
        return results;
    }

    public string AnalyzeImpact(string typeName)
    {
        _logger.LogDebug("Executing AnalyzeImpact for type: {TypeName}", typeName);
        var conn = GetConnection();
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Impact Analysis: {typeName}");
            sb.AppendLine();

            // Find the type's home project
            var homeProjects = QueryTypeList(conn,
                "SELECT type_name, project_name FROM type_with_project " +
                "WHERE type_name = @name AND is_public = 1", typeName);

            if (homeProjects.Count == 0)
            {
                return $"Type '{typeName}' not found.";
            }

            sb.AppendLine("## Defined In");
            foreach (var (_, project) in homeProjects)
                sb.AppendLine($"- {project}");
            sb.AppendLine();

            // Direct references: implementors
            var implementors = QueryTypeList(conn,
                "SELECT DISTINCT v.type_name, v.project_name FROM type_with_project v " +
                "JOIN type_interfaces ti ON ti.type_id = v.id " +
                "WHERE ti.interface_name = @name", typeName);

            // Direct references: injectors
            var injectors = QueryTypeList(conn,
                "SELECT DISTINCT v.type_name, v.project_name FROM type_with_project v " +
                "JOIN type_injected_deps tid ON tid.type_id = v.id " +
                "WHERE tid.dependency_type = @name", typeName);

            // Direct references: base type usage
            var inheritors = QueryTypeList(conn,
                "SELECT DISTINCT type_name, project_name FROM type_with_project " +
                "WHERE base_type = @name", typeName);

            var directProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allReferences = new List<(string TypeName, string Project, string Relation)>();

            foreach (var (t, p) in implementors) { allReferences.Add((t, p, "implements")); directProjects.Add(p); }
            foreach (var (t, p) in injectors) { allReferences.Add((t, p, "injects")); directProjects.Add(p); }
            foreach (var (t, p) in inheritors) { allReferences.Add((t, p, "extends")); directProjects.Add(p); }

            sb.AppendLine("## Directly Affected Types");
            if (allReferences.Count > 0)
            {
                sb.AppendLine("| Type | Project | Relation |");
                sb.AppendLine("| --- | --- | --- |");
                foreach (var (t, p, r) in allReferences.OrderBy(x => x.Project))
                    sb.AppendLine($"| {t} | {p} | {r} |");
            }
            else
            {
                sb.AppendLine("No direct references found.");
            }
            sb.AppendLine();

            // Transitive: projects that depend on directly-affected projects via NuGet
            var transitiveProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directProject in directProjects)
            {
                FindTransitiveDependents(conn, directProject, transitiveProjects, directProjects);
            }
            // Also include transitive deps of home projects
            foreach (var (_, homeProject) in homeProjects)
            {
                directProjects.Add(homeProject);
                FindTransitiveDependents(conn, homeProject, transitiveProjects, directProjects);
            }

            if (transitiveProjects.Count > 0)
            {
                sb.AppendLine("## Transitively Affected Projects");
                sb.AppendLine("Projects that depend on directly-affected projects:");
                sb.AppendLine();
                foreach (var p in transitiveProjects.OrderBy(x => x))
                    sb.AppendLine($"- {p}");
                sb.AppendLine();
            }

            // Summary
            sb.AppendLine("## Summary");
            sb.AppendLine($"- **Direct references:** {allReferences.Count} types across {directProjects.Count} projects");
            sb.AppendLine($"- **Transitive impact:** {transitiveProjects.Count} additional projects");
            sb.AppendLine($"- **Total blast radius:** {directProjects.Count + transitiveProjects.Count} projects");

            return sb.ToString();
        }
        finally { MaybeClose(conn); }
    }

    private static void FindTransitiveDependents(SqliteConnection conn, string projectName, HashSet<string> transitiveProjects, HashSet<string> excludeProjects)
    {
        var queue = new Queue<string>();
        queue.Enqueue(projectName);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { projectName };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT p2.name
                FROM package_references pr
                JOIN assemblies a ON pr.assembly_id = a.id
                JOIN projects p2 ON a.project_id = p2.id
                WHERE pr.is_internal = 1
                  AND pr.package_name IN (
                      SELECT a2.assembly_name FROM assemblies a2
                      JOIN projects p ON a2.project_id = p.id
                      WHERE p.name = @name AND a2.is_test = 0)
                  AND p2.name != @name";
            cmd.Parameters.AddWithValue("@name", current);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var dep = reader.GetString(0);
                if (visited.Add(dep))
                {
                    if (!excludeProjects.Contains(dep))
                        transitiveProjects.Add(dep);
                    queue.Enqueue(dep);
                }
            }
        }
    }

    public string CheckVersionAlignment()
    {
        var conn = GetConnection();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT pr.package_name, pr.version, p.name
                FROM package_references pr
                JOIN assemblies a ON pr.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id
                WHERE pr.is_internal = 0 AND a.is_test = 0 AND pr.version IS NOT NULL
                ORDER BY pr.package_name, pr.version, p.name";

            using var reader = cmd.ExecuteReader();
            // Group: package -> version -> list of projects
            var packages = new Dictionary<string, Dictionary<string, List<string>>>();
            while (reader.Read())
            {
                var pkg = reader.GetString(0);
                var ver = reader.GetString(1);
                var proj = reader.GetString(2);

                if (!packages.ContainsKey(pkg)) packages[pkg] = new Dictionary<string, List<string>>();
                if (!packages[pkg].ContainsKey(ver)) packages[pkg][ver] = new List<string>();
                packages[pkg][ver].Add(proj);
            }

            // Filter to only misaligned packages
            var misaligned = packages.Where(kv => kv.Value.Count > 1)
                .OrderByDescending(kv => ClassifyMismatchSeverity(kv.Value.Keys))
                .ThenBy(kv => kv.Key)
                .ToList();

            if (misaligned.Count == 0)
                return "✅ All packages are version-aligned across projects.";

            var sb = new StringBuilder();
            sb.AppendLine("# NuGet Version Alignment Report");
            sb.AppendLine();
            sb.AppendLine($"Found **{misaligned.Count} packages** with version mismatches:");
            sb.AppendLine();

            foreach (var (pkg, versions) in misaligned)
            {
                var severity = ClassifyMismatchSeverity(versions.Keys);
                var icon = severity == "MAJOR" ? "🔴" : "🟡";
                sb.AppendLine($"### {icon} {pkg} ({severity})");
                sb.AppendLine();
                foreach (var (ver, projects) in versions.OrderBy(v => v.Key))
                {
                    sb.AppendLine($"- **{ver}**: {string.Join(", ", projects)}");
                }
                sb.AppendLine();
            }

            var majorCount = misaligned.Count(m => ClassifyMismatchSeverity(m.Value.Keys) == "MAJOR");
            var minorCount = misaligned.Count - majorCount;
            sb.AppendLine($"**Summary:** {majorCount} major mismatches, {minorCount} minor mismatches");

            return sb.ToString();
        }
        finally { MaybeClose(conn); }
    }

    private static string ClassifyMismatchSeverity(IEnumerable<string> versions)
    {
        var majors = new HashSet<string>();
        foreach (var v in versions)
        {
            var dot = v.IndexOf('.');
            majors.Add(dot > 0 ? v[..dot] : v);
        }
        return majors.Count > 1 ? "MAJOR" : "MINOR";
    }

    public string GetMemoryInfo()
    {
        var conn = GetConnection();
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Memory Database Info\n");

            // DB file size
            if (File.Exists(_config.DbPath))
            {
                var size = new FileInfo(_config.DbPath).Length;
                var sizeStr = size < 1024 * 1024
                    ? $"{size / 1024.0:F1} KB"
                    : $"{size / (1024.0 * 1024.0):F1} MB";
                sb.AppendLine($"**Database path:** `{_config.DbPath}`");
                sb.AppendLine($"**Database size:** {sizeStr}\n");
            }

            // Last scan timestamp from scan_metadata (if table exists)
            try
            {
                using var metaCmd = conn.CreateCommand();
                metaCmd.CommandText = "SELECT MAX(last_scan_utc) FROM scan_metadata";
                var timestamp = metaCmd.ExecuteScalar() as string;
                if (timestamp != null)
                    sb.AppendLine($"**Last scan:** {timestamp}\n");
            }
            catch (SqliteException)
            {
                // scan_metadata table may not exist
            }

            // Row counts per key table
            sb.AppendLine("### Row Counts\n");
            sb.AppendLine("| Table | Rows |");
            sb.AppendLine("| --- | --- |");

            string[] tables = ["projects", "assemblies", "types", "methods", "endpoints", "config_keys"];
            foreach (var table in tables)
            {
                try
                {
                    using var countCmd = conn.CreateCommand();
                    countCmd.CommandText = $"SELECT COUNT(*) FROM {table}";
                    var count = Convert.ToInt64(countCmd.ExecuteScalar());
                    sb.AppendLine($"| {table} | {count} |");
                }
                catch (SqliteException)
                {
                    sb.AppendLine($"| {table} | _(table not found)_ |");
                }
            }

            return sb.ToString().TrimEnd();
        }
        finally { MaybeClose(conn); }
    }

    public string FindUntestedTypes(string? projectName = null)
    {
        var conn = GetConnection();
        try
        {
            using var cmd = conn.CreateCommand();
            var where = new List<string>
            {
                "t.is_public = 1",
                "t.kind IN ('class', 'record')",
                "a.is_test = 0"
            };

            if (!string.IsNullOrEmpty(projectName))
            {
                where.Add("p.name LIKE @proj");
                cmd.Parameters.AddWithValue("@proj", $"%{projectName}%");
            }

            // Find production types that have NO matching test type
            cmd.CommandText = $@"
                SELECT t.type_name, n.namespace_name, p.name, t.file_path
                FROM types t
                JOIN namespaces n ON t.namespace_id = n.id
                JOIN assemblies a ON n.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id
                WHERE {string.Join(" AND ", where)}
                AND NOT EXISTS (
                    SELECT 1 FROM types tt
                    JOIN namespaces tn ON tt.namespace_id = tn.id
                    JOIN assemblies ta ON tn.assembly_id = ta.id
                    WHERE ta.is_test = 1
                    AND (tt.type_name = t.type_name || 'Tests'
                      OR tt.type_name = t.type_name || 'Test'
                      OR tt.type_name = t.type_name || 'Spec')
                )
                AND t.type_name NOT LIKE '%Exception'
                AND t.type_name NOT LIKE '%Attribute'
                AND t.type_name NOT LIKE '%Extensions'
                AND t.type_name NOT LIKE '%Options'
                AND t.type_name NOT LIKE '%Constants'
                AND t.type_name NOT LIKE '%Dto'
                ORDER BY p.name, t.type_name
                LIMIT 200";

            using var reader = cmd.ExecuteReader();
            var lines = new List<string>
            {
                "| Type | Namespace | Project | File |",
                "| --- | --- | --- | --- |"
            };
            while (reader.Read())
            {
                var file = reader.IsDBNull(3) ? "" : reader.GetString(3);
                lines.Add($"| {reader.GetString(0)} | {reader.GetString(1)} | {reader.GetString(2)} | {file} |");
            }

            if (lines.Count == 2)
                return "✅ All production types appear to have test coverage.";

            var count = lines.Count - 2;
            return $"Found **{count} production types** without matching test classes:\n\n" + string.Join("\n", lines);
        }
        finally { MaybeClose(conn); }
    }
}

public class DatabaseNotFoundException : Exception
{
    public DatabaseNotFoundException(string message) : base(message) { }
}
