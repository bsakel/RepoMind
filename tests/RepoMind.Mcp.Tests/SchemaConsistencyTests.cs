using FluentAssertions;
using Microsoft.Data.Sqlite;
using RepoMind.Scanner.Writers;
using Xunit;

namespace RepoMind.Mcp.Tests;

/// <summary>
/// Guards against schema drift between SqliteWriter (production) and TestDatabaseFixture.
/// The fixture now delegates to SqliteWriter.CreateSchemaOn(), so this test verifies
/// that an in-memory database created through that path has the expected tables and indexes.
/// </summary>
public class SchemaConsistencyTests
{
    [Fact]
    public void CreateSchemaOn_ProducesAllExpectedTables()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        SqliteWriter.CreateSchemaOn(connection);

        var tables = QuerySqliteMaster(connection, "table");

        tables.Should().Contain("projects");
        tables.Should().Contain("assemblies");
        tables.Should().Contain("package_references");
        tables.Should().Contain("project_references");
        tables.Should().Contain("namespaces");
        tables.Should().Contain("types");
        tables.Should().Contain("type_interfaces");
        tables.Should().Contain("type_injected_deps");
        tables.Should().Contain("methods");
        tables.Should().Contain("method_parameters");
        tables.Should().Contain("endpoints");
        tables.Should().Contain("scan_metadata");
        tables.Should().Contain("config_keys");
    }

    [Fact]
    public void CreateSchemaOn_ProducesAllExpectedIndexes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        SqliteWriter.CreateSchemaOn(connection);

        var indexes = QuerySqliteMaster(connection, "index");

        indexes.Should().Contain("idx_assemblies_project");
        indexes.Should().Contain("idx_package_refs_assembly");
        indexes.Should().Contain("idx_namespaces_assembly");
        indexes.Should().Contain("idx_types_namespace");
        indexes.Should().Contain("idx_types_name");
        indexes.Should().Contain("idx_package_refs_name");
        indexes.Should().Contain("idx_methods_type");
        indexes.Should().Contain("idx_methods_name");
        indexes.Should().Contain("idx_method_params_method");
        indexes.Should().Contain("idx_endpoints_method");
        indexes.Should().Contain("idx_endpoints_route");
        indexes.Should().Contain("idx_endpoints_kind");
        indexes.Should().Contain("idx_config_keys_project");
        indexes.Should().Contain("idx_config_keys_name");
        indexes.Should().Contain("idx_config_keys_source");
    }

    [Fact]
    public void TestDatabaseFixture_UsesProductionSchema()
    {
        // Create schema via the production path
        using var prodConn = new SqliteConnection("Data Source=:memory:");
        prodConn.Open();
        SqliteWriter.CreateSchemaOn(prodConn);

        // Create schema via the test fixture path
        using var fixture = new TestFixtures.TestDatabaseFixture();

        var prodSchema = GetFullSchema(prodConn);
        var fixtureSchema = GetFullSchema(fixture.Connection);

        fixtureSchema.Should().BeEquivalentTo(prodSchema,
            "the test fixture must use the same schema as SqliteWriter to prevent drift");
    }

    private static List<string> QuerySqliteMaster(SqliteConnection conn, string type)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = @type AND name NOT LIKE 'sqlite_%' ORDER BY name";
        cmd.Parameters.AddWithValue("@type", type);
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private static List<(string type, string name, string sql)> GetFullSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT type, name, sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND sql IS NOT NULL ORDER BY type, name";
        using var reader = cmd.ExecuteReader();
        var schema = new List<(string type, string name, string sql)>();
        while (reader.Read())
            schema.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return schema;
    }
}
