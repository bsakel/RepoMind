using FluentAssertions;
using Microsoft.Data.Sqlite;
using RepoMind.Scanner.Models;
using RepoMind.Scanner.Writers;
using Xunit;

namespace RepoMind.Scanner.Tests.Writers;

public class SqliteWriterTests
{
    private static readonly string[] ExpectedTables =
    [
        "projects", "assemblies", "package_references", "project_references",
        "namespaces", "types", "type_interfaces", "type_injected_deps",
        "methods", "method_parameters", "endpoints", "scan_metadata", "config_keys"
    ];

    [Fact]
    public void Constructor_CreatesAllExpectedTables()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

        try
        {
            using var writer = new SqliteWriter(dbPath);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            var tables = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));

            tables.Should().Contain(ExpectedTables);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void InsertProject_And_InsertAssembly_RoundTrip()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

        try
        {
            using var writer = new SqliteWriter(dbPath);

            var project = new ProjectInfo("TestRepo", "/repos/test", null, null, null);
            var projectId = writer.InsertProject(project);

            var assembly = new AssemblyInfo(
                "TestRepo", "src/App/App.csproj", "App",
                "net10.0", "Exe", false,
                [new PackageRef("Newtonsoft.Json", "13.0.3", false)],
                ["../Lib/Lib.csproj"]);
            var assemblyId = writer.InsertAssembly(projectId, assembly);

            projectId.Should().BeGreaterThan(0);
            assemblyId.Should().BeGreaterThan(0);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var projCmd = conn.CreateCommand();
            projCmd.CommandText = "SELECT name FROM projects WHERE id = @id";
            projCmd.Parameters.AddWithValue("@id", projectId);
            projCmd.ExecuteScalar().Should().Be("TestRepo");

            using var asmCmd = conn.CreateCommand();
            asmCmd.CommandText = "SELECT assembly_name FROM assemblies WHERE id = @id";
            asmCmd.Parameters.AddWithValue("@id", assemblyId);
            asmCmd.ExecuteScalar().Should().Be("App");

            using var pkgCmd = conn.CreateCommand();
            pkgCmd.CommandText = "SELECT package_name FROM package_references WHERE assembly_id = @id";
            pkgCmd.Parameters.AddWithValue("@id", assemblyId);
            pkgCmd.ExecuteScalar().Should().Be("Newtonsoft.Json");

            using var refCmd = conn.CreateCommand();
            refCmd.CommandText = "SELECT referenced_csproj_path FROM project_references WHERE assembly_id = @id";
            refCmd.Parameters.AddWithValue("@id", assemblyId);
            refCmd.ExecuteScalar().Should().Be("../Lib/Lib.csproj");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void DeleteProject_RemovesAllRelatedData()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

        try
        {
            using var writer = new SqliteWriter(dbPath);

            var project = new ProjectInfo("ToDelete", "/repos/del", null, null, null);
            var projectId = writer.InsertProject(project);

            var assembly = new AssemblyInfo(
                "ToDelete", "src/App/App.csproj", "App",
                "net10.0", null, false,
                [new PackageRef("SomePackage", "1.0.0", false)],
                []);
            var assemblyId = writer.InsertAssembly(projectId, assembly);

            var types = new List<TypeInfo>
            {
                new("MyApp.Core", "MyService", "class", true, "MyService.cs", null, [], [], null)
            };
            writer.InsertTypes(assemblyId, types);

            writer.DeleteProject("ToDelete");

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM projects WHERE name = 'ToDelete'";
            ((long)cmd.ExecuteScalar()!).Should().Be(0);

            cmd.CommandText = "SELECT COUNT(*) FROM assemblies";
            ((long)cmd.ExecuteScalar()!).Should().Be(0);

            cmd.CommandText = "SELECT COUNT(*) FROM types";
            ((long)cmd.ExecuteScalar()!).Should().Be(0);

            cmd.CommandText = "SELECT COUNT(*) FROM namespaces";
            ((long)cmd.ExecuteScalar()!).Should().Be(0);

            cmd.CommandText = "SELECT COUNT(*) FROM package_references";
            ((long)cmd.ExecuteScalar()!).Should().Be(0);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void InsertTypes_NamespaceUniqueConstraint_IgnoresDuplicates()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");

        try
        {
            using var writer = new SqliteWriter(dbPath);

            var project = new ProjectInfo("NsTest", "/repos/ns", null, null, null);
            var projectId = writer.InsertProject(project);

            var assembly = new AssemblyInfo(
                "NsTest", "src/App/App.csproj", "App",
                "net10.0", null, false, [], []);
            var assemblyId = writer.InsertAssembly(projectId, assembly);

            var types = new List<TypeInfo>
            {
                new("MyApp.Shared", "TypeA", "class", true, "TypeA.cs", null, [], [], null),
                new("MyApp.Shared", "TypeB", "class", true, "TypeB.cs", null, [], [], null),
            };
            writer.InsertTypes(assemblyId, types);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM namespaces WHERE namespace_name = 'MyApp.Shared'";
            ((long)cmd.ExecuteScalar()!).Should().Be(1);

            cmd.CommandText = "SELECT COUNT(*) FROM types";
            ((long)cmd.ExecuteScalar()!).Should().Be(2);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
