using Microsoft.Data.Sqlite;
using RepoMind.Scanner.Writers;

namespace RepoMind.Mcp.Tests.TestFixtures;

public class TestDatabaseFixture : IDisposable
{
    public SqliteConnection Connection { get; }

    public TestDatabaseFixture()
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();
        SqliteWriter.CreateSchemaOn(Connection);
        SeedData();
    }

    private void SeedData()
    {
        // 3 projects
        ExecuteNonQuery(@"
            INSERT INTO projects (id, name, directory_path, solution_file, git_remote_url) VALUES
            (1, 'acme.core', '/repos/acme.core', 'Common.sln', 'https://github.com/org/common'),
            (2, 'acme.caching', '/repos/acme.caching', 'Caching.sln', 'https://github.com/org/caching'),
            (3, 'acme.web.api', '/repos/acme.web.api', 'CmApi.sln', 'https://github.com/org/cm-api')
        ");

        // 5 assemblies (3 src + 2 test)
        ExecuteNonQuery(@"
            INSERT INTO assemblies (id, project_id, csproj_path, assembly_name, target_framework, output_type, is_test) VALUES
            (1, 1, 'src/Common/Common.csproj', 'Acme.Core', 'net8.0', 'Library', 0),
            (2, 2, 'src/Caching/Caching.csproj', 'Acme.Caching', 'net8.0', 'Library', 0),
            (3, 3, 'src/CmApi/CmApi.csproj', 'Acme.Web.Api', 'net8.0', 'Exe', 0),
            (4, 1, 'tests/Common.Tests/Common.Tests.csproj', 'Acme.Core.Tests', 'net8.0', 'Library', 1),
            (5, 2, 'tests/Caching.Tests/Caching.Tests.csproj', 'Acme.Caching.Tests', 'net8.0', 'Library', 1)
        ");

        // 10 package references
        ExecuteNonQuery(@"
            INSERT INTO package_references (assembly_id, package_name, version, is_internal) VALUES
            (2, 'Acme.Core', '1.2.0', 1),
            (3, 'Acme.Core', '1.2.0', 1),
            (3, 'Acme.Caching', '2.0.0', 1),
            (1, 'Newtonsoft.Json', '13.0.3', 0),
            (2, 'Microsoft.Extensions.Caching.Memory', '8.0.0', 0),
            (2, 'Newtonsoft.Json', '13.0.3', 0),
            (3, 'HotChocolate', '13.9.0', 0),
            (3, 'HotChocolate', '14.0.0', 0),
            (3, 'Newtonsoft.Json', '13.0.1', 0),
            (1, 'Microsoft.Extensions.Logging', '8.0.0', 0)
        ");

        // 4 namespaces (production) + 2 test namespaces
        ExecuteNonQuery(@"
            INSERT INTO namespaces (id, assembly_id, namespace_name) VALUES
            (1, 1, 'Acme.Core'),
            (2, 1, 'Acme.Core.Models'),
            (3, 2, 'Acme.Caching'),
            (4, 3, 'Acme.Web.Api'),
            (5, 4, 'Acme.Core.Tests'),
            (6, 5, 'Acme.Caching.Tests')
        ");

        // 15 production types + 3 test types
        ExecuteNonQuery(@"
            INSERT INTO types (id, namespace_id, type_name, kind, is_public, file_path, base_type, summary_comment) VALUES
            (1, 1, 'IRepository', 'interface', 1, 'src/Common/IRepository.cs', NULL, 'Base repository interface'),
            (2, 1, 'IEntityService', 'interface', 1, 'src/Common/IEntityService.cs', NULL, NULL),
            (3, 2, 'BaseEntity', 'class', 1, 'src/Common/Models/BaseEntity.cs', NULL, 'Base entity class'),
            (4, 2, 'ContentItem', 'class', 1, 'src/Common/Models/ContentItem.cs', 'BaseEntity', NULL),
            (5, 2, 'EntityStatus', 'enum', 1, 'src/Common/Models/EntityStatus.cs', NULL, NULL),
            (6, 3, 'ICoherentCache', 'interface', 1, 'src/Caching/ICoherentCache.cs', NULL, 'Coherent cache interface'),
            (7, 3, 'CoherentCacheService', 'class', 1, 'src/Caching/CoherentCacheService.cs', NULL, NULL),
            (8, 3, 'CacheEvictionHandler', 'class', 1, 'src/Caching/CacheEvictionHandler.cs', NULL, NULL),
            (9, 3, 'ICacheEvictionHandler', 'interface', 1, 'src/Caching/ICacheEvictionHandler.cs', NULL, NULL),
            (10, 3, 'CacheOptions', 'record', 1, 'src/Caching/CacheOptions.cs', NULL, NULL),
            (11, 4, 'ContentController', 'class', 1, 'src/CmApi/ContentController.cs', NULL, NULL),
            (12, 4, 'PublishingService', 'class', 1, 'src/CmApi/PublishingService.cs', NULL, NULL),
            (13, 4, 'IPublishingService', 'interface', 1, 'src/CmApi/IPublishingService.cs', NULL, NULL),
            (14, 4, 'ApiStartup', 'class', 0, 'src/CmApi/ApiStartup.cs', NULL, NULL),
            (15, 2, 'ItemRecord', 'record', 1, 'src/Common/Models/ItemRecord.cs', NULL, NULL),
            (16, 5, 'BaseEntityTests', 'class', 1, 'tests/Common/BaseEntityTests.cs', NULL, NULL),
            (17, 5, 'ContentItemTests', 'class', 1, 'tests/Common/ContentItemTests.cs', NULL, NULL),
            (18, 6, 'CoherentCacheServiceTests', 'class', 1, 'tests/Caching/CoherentCacheServiceTests.cs', NULL, NULL)
        ");

        // 6 interface implementations
        ExecuteNonQuery(@"
            INSERT INTO type_interfaces (type_id, interface_name) VALUES
            (7, 'ICoherentCache'),
            (8, 'ICacheEvictionHandler'),
            (12, 'IPublishingService'),
            (11, 'IRepository'),
            (4, 'IEntityService'),
            (7, 'IDisposable')
        ");

        // 8 injected dependencies
        ExecuteNonQuery(@"
            INSERT INTO type_injected_deps (type_id, dependency_type) VALUES
            (7, 'ILogger<CoherentCacheService>'),
            (7, 'IOptions<CacheOptions>'),
            (8, 'ICoherentCache'),
            (8, 'ILogger<CacheEvictionHandler>'),
            (11, 'ICoherentCache'),
            (11, 'IPublishingService'),
            (12, 'IRepository'),
            (12, 'ICoherentCache')
        ");

        // Methods on ContentController (type 11)
        ExecuteNonQuery(@"
            INSERT INTO methods (id, type_id, method_name, return_type, is_public, is_static) VALUES
            (1, 11, 'GetContent', 'Task<IActionResult>', 1, 0),
            (2, 11, 'CreateContent', 'Task<IActionResult>', 1, 0),
            (3, 11, 'DeleteContent', 'Task<IActionResult>', 1, 0),
            (4, 12, 'PublishAsync', 'Task<PublishResult>', 1, 0),
            (5, 12, 'UnpublishAsync', 'Task<bool>', 1, 0),
            (6, 7, 'GetAsync', 'Task<T>', 1, 0),
            (7, 7, 'SetAsync', 'Task', 1, 0),
            (8, 7, 'EvictAsync', 'Task', 1, 0)
        ");

        // Method parameters
        ExecuteNonQuery(@"
            INSERT INTO method_parameters (method_id, param_name, param_type, position) VALUES
            (1, 'id', 'string', 0),
            (2, 'item', 'ContentItem', 0),
            (2, 'cancellationToken', 'CancellationToken', 1),
            (3, 'id', 'string', 0),
            (4, 'contentId', 'string', 0),
            (4, 'options', 'PublishOptions', 1),
            (5, 'contentId', 'string', 0)
        ");

        // REST endpoints on ContentController
        ExecuteNonQuery(@"
            INSERT INTO endpoints (type_id, method_id, http_method, route_template, endpoint_kind) VALUES
            (11, 1, 'GET', 'api/content/{id}', 'REST'),
            (11, 2, 'POST', 'api/content', 'REST'),
            (11, 3, 'DELETE', 'api/content/{id}', 'REST'),
            (12, 4, 'POST', 'api/publish/{contentId}', 'REST')
        ");

        // Config keys
        ExecuteNonQuery(@"
            INSERT INTO config_keys (project_name, source, key_name, default_value, file_path) VALUES
            ('acme.caching', 'appsettings', 'Caching:DefaultTtlSeconds', '300', 'appsettings.json'),
            ('acme.caching', 'appsettings', 'Caching:RedisConnectionString', NULL, 'appsettings.json'),
            ('acme.web.api', 'appsettings', 'CosmosDb:ConnectionString', NULL, 'appsettings.json'),
            ('acme.web.api', 'appsettings', 'CosmosDb:DatabaseName', 'content-db', 'appsettings.json'),
            ('acme.web.api', 'env_var', 'ASPNETCORE_ENVIRONMENT', NULL, 'src/CmApi/Program.cs'),
            ('acme.web.api', 'IConfiguration', 'Kafka:BootstrapServers', NULL, 'src/CmApi/Startup.cs'),
            ('acme.core', 'env_var', 'LOG_LEVEL', NULL, 'src/Common/Logging.cs')
        ");

        // Create temp views that QueryService.EnsureViews() requires
        ExecuteNonQuery(RepoMind.Mcp.Services.QueryService.CreateTypeWithProjectView);

        // Prevent accidental writes from tests
        using var pragmaCmd = Connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA query_only = ON;";
        pragmaCmd.ExecuteNonQuery();
    }

    private void ExecuteNonQuery(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}
