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
        ApplyPragmas(_connection);
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
        ApplyPragmas(conn);
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
        using var tx = _connection.BeginTransaction();
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
        tx.Commit();
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

    /// <summary>
    /// Delete the stored scan hash for a project, forcing it to be rescanned on next incremental run.
    /// </summary>
    public static void DeleteScanHash(string dbPath, string projectName)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='scan_metadata'";
        if (checkCmd.ExecuteScalar() == null) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM scan_metadata WHERE project_name = @name";
        cmd.Parameters.AddWithValue("@name", projectName);
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
                namespace_name TEXT NOT NULL,
                UNIQUE(assembly_id, namespace_name)
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
        return ExecuteInsertAndGetId(cmd);
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

        var assemblyId = ExecuteInsertAndGetId(cmd);

        using var pkgCmd = _connection.CreateCommand();
        pkgCmd.CommandText = @"
            INSERT INTO package_references (assembly_id, package_name, version, is_internal)
            VALUES (@aid, @name, @ver, @internal);
        ";
        var pkgAid = pkgCmd.Parameters.Add(new SqliteParameter("@aid", SqliteType.Integer));
        var pkgName = pkgCmd.Parameters.Add(new SqliteParameter("@name", SqliteType.Text));
        var pkgVer = pkgCmd.Parameters.Add(new SqliteParameter("@ver", SqliteType.Text));
        var pkgInternal = pkgCmd.Parameters.Add(new SqliteParameter("@internal", SqliteType.Integer));

        foreach (var pkg in assembly.PackageReferences)
        {
            pkgAid.Value = assemblyId;
            pkgName.Value = pkg.Name;
            pkgVer.Value = (object?)pkg.Version ?? DBNull.Value;
            pkgInternal.Value = pkg.IsInternal ? 1 : 0;
            pkgCmd.ExecuteNonQuery();
        }

        using var refCmd = _connection.CreateCommand();
        refCmd.CommandText = @"
            INSERT INTO project_references (assembly_id, referenced_csproj_path)
            VALUES (@aid, @path);
        ";
        var refAid = refCmd.Parameters.Add(new SqliteParameter("@aid", SqliteType.Integer));
        var refPath = refCmd.Parameters.Add(new SqliteParameter("@path", SqliteType.Text));

        foreach (var projRef in assembly.ProjectReferences)
        {
            refAid.Value = assemblyId;
            refPath.Value = projRef;
            refCmd.ExecuteNonQuery();
        }

        return assemblyId;
    }

    public void InsertTypes(long assemblyId, List<TypeInfo> types)
    {
        var byNamespace = types.GroupBy(t => t.NamespaceName);

        // Reuse commands across iterations
        using var nsInsertCmd = _connection.CreateCommand();
        nsInsertCmd.CommandText = "INSERT OR IGNORE INTO namespaces (assembly_id, namespace_name) VALUES (@aid, @ns)";
        var nsAid = nsInsertCmd.Parameters.Add(new SqliteParameter("@aid", SqliteType.Integer));
        var nsNs = nsInsertCmd.Parameters.Add(new SqliteParameter("@ns", SqliteType.Text));

        using var nsSelectCmd = _connection.CreateCommand();
        nsSelectCmd.CommandText = "SELECT id FROM namespaces WHERE assembly_id = @aid AND namespace_name = @ns";
        var nsSelAid = nsSelectCmd.Parameters.Add(new SqliteParameter("@aid", SqliteType.Integer));
        var nsSelNs = nsSelectCmd.Parameters.Add(new SqliteParameter("@ns", SqliteType.Text));

        using var tCmd = _connection.CreateCommand();
        tCmd.CommandText = @"
            INSERT INTO types (namespace_id, type_name, kind, is_public, file_path, base_type, summary_comment)
            VALUES (@nsid, @name, @kind, @pub, @file, @base, @summary);
            SELECT last_insert_rowid();
        ";
        var tNsid = tCmd.Parameters.Add(new SqliteParameter("@nsid", SqliteType.Integer));
        var tName = tCmd.Parameters.Add(new SqliteParameter("@name", SqliteType.Text));
        var tKind = tCmd.Parameters.Add(new SqliteParameter("@kind", SqliteType.Text));
        var tPub = tCmd.Parameters.Add(new SqliteParameter("@pub", SqliteType.Integer));
        var tFile = tCmd.Parameters.Add(new SqliteParameter("@file", SqliteType.Text));
        var tBase = tCmd.Parameters.Add(new SqliteParameter("@base", SqliteType.Text));
        var tSummary = tCmd.Parameters.Add(new SqliteParameter("@summary", SqliteType.Text));

        using var ifCmd = _connection.CreateCommand();
        ifCmd.CommandText = "INSERT OR IGNORE INTO type_interfaces (type_id, interface_name) VALUES (@tid, @iface);";
        var ifTid = ifCmd.Parameters.Add(new SqliteParameter("@tid", SqliteType.Integer));
        var ifIface = ifCmd.Parameters.Add(new SqliteParameter("@iface", SqliteType.Text));

        using var depCmd = _connection.CreateCommand();
        depCmd.CommandText = "INSERT OR IGNORE INTO type_injected_deps (type_id, dependency_type) VALUES (@tid, @dep);";
        var depTid = depCmd.Parameters.Add(new SqliteParameter("@tid", SqliteType.Integer));
        var depDep = depCmd.Parameters.Add(new SqliteParameter("@dep", SqliteType.Text));

        using var mCmd = _connection.CreateCommand();
        mCmd.CommandText = @"
            INSERT INTO methods (type_id, method_name, return_type, is_public, is_static)
            VALUES (@tid, @name, @ret, @pub, @stat);
            SELECT last_insert_rowid();
        ";
        var mTid = mCmd.Parameters.Add(new SqliteParameter("@tid", SqliteType.Integer));
        var mName = mCmd.Parameters.Add(new SqliteParameter("@name", SqliteType.Text));
        var mRet = mCmd.Parameters.Add(new SqliteParameter("@ret", SqliteType.Text));
        var mPub = mCmd.Parameters.Add(new SqliteParameter("@pub", SqliteType.Integer));
        var mStat = mCmd.Parameters.Add(new SqliteParameter("@stat", SqliteType.Integer));

        using var pCmd = _connection.CreateCommand();
        pCmd.CommandText = @"
            INSERT INTO method_parameters (method_id, param_name, param_type, position)
            VALUES (@mid, @name, @type, @pos);
        ";
        var pMid = pCmd.Parameters.Add(new SqliteParameter("@mid", SqliteType.Integer));
        var pName = pCmd.Parameters.Add(new SqliteParameter("@name", SqliteType.Text));
        var pType = pCmd.Parameters.Add(new SqliteParameter("@type", SqliteType.Text));
        var pPos = pCmd.Parameters.Add(new SqliteParameter("@pos", SqliteType.Integer));

        using var eCmd = _connection.CreateCommand();
        eCmd.CommandText = @"
            INSERT INTO endpoints (type_id, method_id, http_method, route_template, endpoint_kind)
            VALUES (@tid, @mid, @http, @route, @kind);
        ";
        var eTid = eCmd.Parameters.Add(new SqliteParameter("@tid", SqliteType.Integer));
        var eMid = eCmd.Parameters.Add(new SqliteParameter("@mid", SqliteType.Integer));
        var eHttp = eCmd.Parameters.Add(new SqliteParameter("@http", SqliteType.Text));
        var eRoute = eCmd.Parameters.Add(new SqliteParameter("@route", SqliteType.Text));
        var eKind = eCmd.Parameters.Add(new SqliteParameter("@kind", SqliteType.Text));

        foreach (var nsGroup in byNamespace)
        {
            nsAid.Value = assemblyId;
            nsNs.Value = nsGroup.Key;
            nsInsertCmd.ExecuteNonQuery();

            nsSelAid.Value = assemblyId;
            nsSelNs.Value = nsGroup.Key;
            var nsResult = nsSelectCmd.ExecuteScalar() ?? throw new InvalidOperationException("Namespace row not found after INSERT OR IGNORE");
            var nsId = (long)nsResult;

            foreach (var type in nsGroup)
            {
                tNsid.Value = nsId;
                tName.Value = type.TypeName;
                tKind.Value = type.Kind;
                tPub.Value = type.IsPublic ? 1 : 0;
                tFile.Value = (object?)type.FilePath ?? DBNull.Value;
                tBase.Value = (object?)type.BaseType ?? DBNull.Value;
                tSummary.Value = (object?)type.SummaryComment ?? DBNull.Value;
                var typeId = ExecuteInsertAndGetId(tCmd);

                foreach (var iface in type.ImplementedInterfaces)
                {
                    ifTid.Value = typeId;
                    ifIface.Value = iface;
                    ifCmd.ExecuteNonQuery();
                }

                foreach (var dep in type.InjectedDependencies)
                {
                    depTid.Value = typeId;
                    depDep.Value = dep;
                    depCmd.ExecuteNonQuery();
                }

                if (type.Methods != null)
                {
                    foreach (var method in type.Methods)
                    {
                        mTid.Value = typeId;
                        mName.Value = method.MethodName;
                        mRet.Value = method.ReturnType;
                        mPub.Value = method.IsPublic ? 1 : 0;
                        mStat.Value = method.IsStatic ? 1 : 0;
                        var methodId = ExecuteInsertAndGetId(mCmd);

                        foreach (var param in method.Parameters)
                        {
                            pMid.Value = methodId;
                            pName.Value = param.Name;
                            pType.Value = param.Type;
                            pPos.Value = param.Position;
                            pCmd.ExecuteNonQuery();
                        }

                        foreach (var ep in method.Endpoints)
                        {
                            eTid.Value = typeId;
                            eMid.Value = methodId;
                            eHttp.Value = ep.HttpMethod;
                            eRoute.Value = (object?)ep.RouteTemplate ?? DBNull.Value;
                            eKind.Value = ep.Kind;
                            eCmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }
    }

    public void InsertConfigKeys(string projectName, List<Parsers.ConfigEntry> entries)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO config_keys (project_name, source, key_name, default_value, file_path)
            VALUES (@proj, @src, @key, @val, @file)";
        var pProj = cmd.Parameters.Add(new SqliteParameter("@proj", SqliteType.Text));
        var pSrc = cmd.Parameters.Add(new SqliteParameter("@src", SqliteType.Text));
        var pKey = cmd.Parameters.Add(new SqliteParameter("@key", SqliteType.Text));
        var pVal = cmd.Parameters.Add(new SqliteParameter("@val", SqliteType.Text));
        var pFile = cmd.Parameters.Add(new SqliteParameter("@file", SqliteType.Text));

        foreach (var entry in entries)
        {
            pProj.Value = projectName;
            pSrc.Value = entry.Source;
            pKey.Value = entry.KeyName;
            pVal.Value = (object?)entry.DefaultValue ?? DBNull.Value;
            pFile.Value = entry.FilePath;
            cmd.ExecuteNonQuery();
        }
    }

    public SqliteTransaction BeginBulkTransaction() => _connection.BeginTransaction();

    private static long ExecuteInsertAndGetId(SqliteCommand cmd)
    {
        var result = cmd.ExecuteScalar() ?? throw new InvalidOperationException("INSERT did not return row ID");
        return (long)result;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA synchronous = NORMAL";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA foreign_keys = ON";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA cache_size = -64000";
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
