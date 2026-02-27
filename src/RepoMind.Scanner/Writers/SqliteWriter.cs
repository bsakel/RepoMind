using Microsoft.Data.Sqlite;
using RepoMind.Scanner.Models;
using Serilog;

namespace RepoMind.Scanner.Writers;

public class SqliteWriter : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteWriter(string dbPath)
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        CreateSchema();
    }

    private SqliteWriter(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// Open an existing database for incremental updates.
    /// </summary>
    public static SqliteWriter OpenExisting(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        // Ensure new tables exist (for DBs created before methods/endpoints were added)
        EnsureNewTables(conn);
        return new SqliteWriter(conn);
    }

    /// <summary>
    /// Read stored scan hashes from an existing database.
    /// </summary>
    public static Dictionary<string, string> ReadScanHashes(string dbPath)
    {
        var hashes = new Dictionary<string, string>();
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // Check if scan_metadata table exists
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='scan_metadata'";
        if (checkCmd.ExecuteScalar() == null) return hashes;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT project_name, file_hash FROM scan_metadata";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            hashes[reader.GetString(0)] = reader.GetString(1);
        return hashes;
    }

    /// <summary>
    /// Delete all data for a project (for incremental re-insert).
    /// </summary>
    public void DeleteProject(string projectName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM endpoints WHERE type_id IN (
                SELECT t.id FROM types t
                JOIN namespaces n ON t.namespace_id = n.id
                JOIN assemblies a ON n.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM method_parameters WHERE method_id IN (
                SELECT m.id FROM methods m
                JOIN types t ON m.type_id = t.id
                JOIN namespaces n ON t.namespace_id = n.id
                JOIN assemblies a ON n.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM methods WHERE type_id IN (
                SELECT t.id FROM types t
                JOIN namespaces n ON t.namespace_id = n.id
                JOIN assemblies a ON n.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM type_injected_deps WHERE type_id IN (
                SELECT t.id FROM types t
                JOIN namespaces n ON t.namespace_id = n.id
                JOIN assemblies a ON n.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM type_interfaces WHERE type_id IN (
                SELECT t.id FROM types t
                JOIN namespaces n ON t.namespace_id = n.id
                JOIN assemblies a ON n.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM types WHERE namespace_id IN (
                SELECT n.id FROM namespaces n
                JOIN assemblies a ON n.assembly_id = a.id
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM namespaces WHERE assembly_id IN (
                SELECT a.id FROM assemblies a
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM package_references WHERE assembly_id IN (
                SELECT a.id FROM assemblies a
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM project_references WHERE assembly_id IN (
                SELECT a.id FROM assemblies a
                JOIN projects p ON a.project_id = p.id WHERE p.name = @name);
            DELETE FROM assemblies WHERE project_id IN (
                SELECT id FROM projects WHERE name = @name);
            DELETE FROM config_keys WHERE project_name = @name;
            DELETE FROM projects WHERE name = @name;
        ";
        cmd.Parameters.AddWithValue("@name", projectName);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Store or update the scan hash for a project.
    /// </summary>
    public void UpsertScanHash(string projectName, string fileHash)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO scan_metadata (project_name, file_hash, last_scan_utc)
            VALUES (@name, @hash, @ts)
            ON CONFLICT(project_name) DO UPDATE SET file_hash = @hash, last_scan_utc = @ts";
        cmd.Parameters.AddWithValue("@name", projectName);
        cmd.Parameters.AddWithValue("@hash", fileHash);
        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void EnsureNewTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS methods (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type_id INTEGER NOT NULL REFERENCES types(id),
                method_name TEXT NOT NULL,
                return_type TEXT NOT NULL,
                is_public INTEGER NOT NULL DEFAULT 1,
                is_static INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS method_parameters (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                method_id INTEGER NOT NULL REFERENCES methods(id),
                param_name TEXT NOT NULL,
                param_type TEXT NOT NULL,
                position INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS endpoints (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type_id INTEGER NOT NULL REFERENCES types(id),
                method_id INTEGER NOT NULL REFERENCES methods(id),
                http_method TEXT NOT NULL,
                route_template TEXT,
                endpoint_kind TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS scan_metadata (
                project_name TEXT PRIMARY KEY,
                file_hash TEXT NOT NULL,
                last_scan_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_methods_type ON methods(type_id);
            CREATE INDEX IF NOT EXISTS idx_methods_name ON methods(method_name);
            CREATE INDEX IF NOT EXISTS idx_method_params_method ON method_parameters(method_id);
            CREATE INDEX IF NOT EXISTS idx_endpoints_method ON endpoints(method_id);
            CREATE INDEX IF NOT EXISTS idx_endpoints_route ON endpoints(route_template);
            CREATE INDEX IF NOT EXISTS idx_endpoints_kind ON endpoints(endpoint_kind);
            CREATE TABLE IF NOT EXISTS config_keys (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_name TEXT NOT NULL,
                source TEXT NOT NULL,
                key_name TEXT NOT NULL,
                default_value TEXT,
                file_path TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_config_keys_project ON config_keys(project_name);
            CREATE INDEX IF NOT EXISTS idx_config_keys_name ON config_keys(key_name);
            CREATE INDEX IF NOT EXISTS idx_config_keys_source ON config_keys(source);
        ";
        cmd.ExecuteNonQuery();
    }

    private void CreateSchema()
    {
        var sql = @"
            CREATE TABLE projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                directory_path TEXT NOT NULL,
                solution_file TEXT,
                git_remote_url TEXT,
                default_branch TEXT
            );

            CREATE TABLE assemblies (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL REFERENCES projects(id),
                csproj_path TEXT NOT NULL,
                assembly_name TEXT NOT NULL,
                target_framework TEXT,
                output_type TEXT,
                is_test INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE package_references (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                assembly_id INTEGER NOT NULL REFERENCES assemblies(id),
                package_name TEXT NOT NULL,
                version TEXT,
                is_internal INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE project_references (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                assembly_id INTEGER NOT NULL REFERENCES assemblies(id),
                referenced_csproj_path TEXT NOT NULL
            );

            CREATE TABLE namespaces (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                assembly_id INTEGER NOT NULL REFERENCES assemblies(id),
                namespace_name TEXT NOT NULL
            );

            CREATE TABLE types (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                namespace_id INTEGER NOT NULL REFERENCES namespaces(id),
                type_name TEXT NOT NULL,
                kind TEXT NOT NULL,
                is_public INTEGER NOT NULL DEFAULT 0,
                file_path TEXT,
                base_type TEXT,
                summary_comment TEXT
            );

            CREATE TABLE type_interfaces (
                type_id INTEGER NOT NULL REFERENCES types(id),
                interface_name TEXT NOT NULL,
                PRIMARY KEY (type_id, interface_name)
            );

            CREATE TABLE type_injected_deps (
                type_id INTEGER NOT NULL REFERENCES types(id),
                dependency_type TEXT NOT NULL,
                PRIMARY KEY (type_id, dependency_type)
            );

            CREATE INDEX idx_assemblies_project ON assemblies(project_id);
            CREATE INDEX idx_package_refs_assembly ON package_references(assembly_id);
            CREATE INDEX idx_namespaces_assembly ON namespaces(assembly_id);
            CREATE INDEX idx_types_namespace ON types(namespace_id);
            CREATE INDEX idx_types_name ON types(type_name);
            CREATE INDEX idx_package_refs_name ON package_references(package_name);

            CREATE TABLE methods (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type_id INTEGER NOT NULL REFERENCES types(id),
                method_name TEXT NOT NULL,
                return_type TEXT NOT NULL,
                is_public INTEGER NOT NULL DEFAULT 1,
                is_static INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE method_parameters (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                method_id INTEGER NOT NULL REFERENCES methods(id),
                param_name TEXT NOT NULL,
                param_type TEXT NOT NULL,
                position INTEGER NOT NULL
            );

            CREATE TABLE endpoints (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type_id INTEGER NOT NULL REFERENCES types(id),
                method_id INTEGER NOT NULL REFERENCES methods(id),
                http_method TEXT NOT NULL,
                route_template TEXT,
                endpoint_kind TEXT NOT NULL
            );

            CREATE INDEX idx_methods_type ON methods(type_id);
            CREATE INDEX idx_methods_name ON methods(method_name);
            CREATE INDEX idx_method_params_method ON method_parameters(method_id);
            CREATE INDEX idx_endpoints_method ON endpoints(method_id);
            CREATE INDEX idx_endpoints_route ON endpoints(route_template);
            CREATE INDEX idx_endpoints_kind ON endpoints(endpoint_kind);

            CREATE TABLE scan_metadata (
                project_name TEXT PRIMARY KEY,
                file_hash TEXT NOT NULL,
                last_scan_utc TEXT NOT NULL
            );

            CREATE TABLE config_keys (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_name TEXT NOT NULL,
                source TEXT NOT NULL,
                key_name TEXT NOT NULL,
                default_value TEXT,
                file_path TEXT NOT NULL
            );

            CREATE INDEX idx_config_keys_project ON config_keys(project_name);
            CREATE INDEX idx_config_keys_name ON config_keys(key_name);
            CREATE INDEX idx_config_keys_source ON config_keys(source);
        ";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        Log.Information("SQLite schema created.");
    }

    public long InsertProject(ProjectInfo project)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO projects (name, directory_path, solution_file, git_remote_url, default_branch)
            VALUES (@name, @dir, @sln, @remote, @branch);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@name", project.Name);
        cmd.Parameters.AddWithValue("@dir", project.DirectoryPath);
        cmd.Parameters.AddWithValue("@sln", (object?)project.SolutionFile ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@remote", (object?)project.GitRemoteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@branch", (object?)project.DefaultBranch ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public long InsertAssembly(long projectId, AssemblyInfo assembly)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO assemblies (project_id, csproj_path, assembly_name, target_framework, output_type, is_test)
            VALUES (@pid, @csproj, @name, @tf, @ot, @test);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@csproj", assembly.CsprojPath);
        cmd.Parameters.AddWithValue("@name", assembly.AssemblyName);
        cmd.Parameters.AddWithValue("@tf", (object?)assembly.TargetFramework ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ot", (object?)assembly.OutputType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@test", assembly.IsTest ? 1 : 0);

        var assemblyId = (long)cmd.ExecuteScalar()!;

        foreach (var pkg in assembly.PackageReferences)
        {
            using var pkgCmd = _connection.CreateCommand();
            pkgCmd.CommandText = @"
                INSERT INTO package_references (assembly_id, package_name, version, is_internal)
                VALUES (@aid, @name, @ver, @internal);
            ";
            pkgCmd.Parameters.AddWithValue("@aid", assemblyId);
            pkgCmd.Parameters.AddWithValue("@name", pkg.Name);
            pkgCmd.Parameters.AddWithValue("@ver", (object?)pkg.Version ?? DBNull.Value);
            pkgCmd.Parameters.AddWithValue("@internal", pkg.IsInternal ? 1 : 0);
            pkgCmd.ExecuteNonQuery();
        }

        foreach (var projRef in assembly.ProjectReferences)
        {
            using var refCmd = _connection.CreateCommand();
            refCmd.CommandText = @"
                INSERT INTO project_references (assembly_id, referenced_csproj_path)
                VALUES (@aid, @path);
            ";
            refCmd.Parameters.AddWithValue("@aid", assemblyId);
            refCmd.Parameters.AddWithValue("@path", projRef);
            refCmd.ExecuteNonQuery();
        }

        return assemblyId;
    }

    public void InsertTypes(long assemblyId, List<TypeInfo> types)
    {
        var byNamespace = types.GroupBy(t => t.NamespaceName);
        foreach (var nsGroup in byNamespace)
        {
            using var nsCmd = _connection.CreateCommand();
            nsCmd.CommandText = @"
                INSERT INTO namespaces (assembly_id, namespace_name) VALUES (@aid, @ns);
                SELECT last_insert_rowid();
            ";
            nsCmd.Parameters.AddWithValue("@aid", assemblyId);
            nsCmd.Parameters.AddWithValue("@ns", nsGroup.Key);
            var nsId = (long)nsCmd.ExecuteScalar()!;

            foreach (var type in nsGroup)
            {
                using var tCmd = _connection.CreateCommand();
                tCmd.CommandText = @"
                    INSERT INTO types (namespace_id, type_name, kind, is_public, file_path, base_type, summary_comment)
                    VALUES (@nsid, @name, @kind, @pub, @file, @base, @summary);
                    SELECT last_insert_rowid();
                ";
                tCmd.Parameters.AddWithValue("@nsid", nsId);
                tCmd.Parameters.AddWithValue("@name", type.TypeName);
                tCmd.Parameters.AddWithValue("@kind", type.Kind);
                tCmd.Parameters.AddWithValue("@pub", type.IsPublic ? 1 : 0);
                tCmd.Parameters.AddWithValue("@file", (object?)type.FilePath ?? DBNull.Value);
                tCmd.Parameters.AddWithValue("@base", (object?)type.BaseType ?? DBNull.Value);
                tCmd.Parameters.AddWithValue("@summary", (object?)type.SummaryComment ?? DBNull.Value);
                var typeId = (long)tCmd.ExecuteScalar()!;

                foreach (var iface in type.ImplementedInterfaces)
                {
                    using var ifCmd = _connection.CreateCommand();
                    ifCmd.CommandText = "INSERT OR IGNORE INTO type_interfaces (type_id, interface_name) VALUES (@tid, @iface);";
                    ifCmd.Parameters.AddWithValue("@tid", typeId);
                    ifCmd.Parameters.AddWithValue("@iface", iface);
                    ifCmd.ExecuteNonQuery();
                }

                foreach (var dep in type.InjectedDependencies)
                {
                    using var depCmd = _connection.CreateCommand();
                    depCmd.CommandText = "INSERT OR IGNORE INTO type_injected_deps (type_id, dependency_type) VALUES (@tid, @dep);";
                    depCmd.Parameters.AddWithValue("@tid", typeId);
                    depCmd.Parameters.AddWithValue("@dep", dep);
                    depCmd.ExecuteNonQuery();
                }

                if (type.Methods != null)
                {
                    foreach (var method in type.Methods)
                    {
                        using var mCmd = _connection.CreateCommand();
                        mCmd.CommandText = @"
                            INSERT INTO methods (type_id, method_name, return_type, is_public, is_static)
                            VALUES (@tid, @name, @ret, @pub, @stat);
                            SELECT last_insert_rowid();
                        ";
                        mCmd.Parameters.AddWithValue("@tid", typeId);
                        mCmd.Parameters.AddWithValue("@name", method.MethodName);
                        mCmd.Parameters.AddWithValue("@ret", method.ReturnType);
                        mCmd.Parameters.AddWithValue("@pub", method.IsPublic ? 1 : 0);
                        mCmd.Parameters.AddWithValue("@stat", method.IsStatic ? 1 : 0);
                        var methodId = (long)mCmd.ExecuteScalar()!;

                        foreach (var param in method.Parameters)
                        {
                            using var pCmd = _connection.CreateCommand();
                            pCmd.CommandText = @"
                                INSERT INTO method_parameters (method_id, param_name, param_type, position)
                                VALUES (@mid, @name, @type, @pos);
                            ";
                            pCmd.Parameters.AddWithValue("@mid", methodId);
                            pCmd.Parameters.AddWithValue("@name", param.Name);
                            pCmd.Parameters.AddWithValue("@type", param.Type);
                            pCmd.Parameters.AddWithValue("@pos", param.Position);
                            pCmd.ExecuteNonQuery();
                        }

                        foreach (var ep in method.Endpoints)
                        {
                            using var eCmd = _connection.CreateCommand();
                            eCmd.CommandText = @"
                                INSERT INTO endpoints (type_id, method_id, http_method, route_template, endpoint_kind)
                                VALUES (@tid, @mid, @http, @route, @kind);
                            ";
                            eCmd.Parameters.AddWithValue("@tid", typeId);
                            eCmd.Parameters.AddWithValue("@mid", methodId);
                            eCmd.Parameters.AddWithValue("@http", ep.HttpMethod);
                            eCmd.Parameters.AddWithValue("@route", (object?)ep.RouteTemplate ?? DBNull.Value);
                            eCmd.Parameters.AddWithValue("@kind", ep.Kind);
                            eCmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }
    }

    public void InsertConfigKeys(string projectName, List<Parsers.ConfigEntry> entries)
    {
        foreach (var entry in entries)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO config_keys (project_name, source, key_name, default_value, file_path)
                VALUES (@proj, @src, @key, @val, @file)";
            cmd.Parameters.AddWithValue("@proj", projectName);
            cmd.Parameters.AddWithValue("@src", entry.Source);
            cmd.Parameters.AddWithValue("@key", entry.KeyName);
            cmd.Parameters.AddWithValue("@val", (object?)entry.DefaultValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@file", entry.FilePath);
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();
}
