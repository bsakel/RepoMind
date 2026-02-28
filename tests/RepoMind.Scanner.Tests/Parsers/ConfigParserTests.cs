using FluentAssertions;
using RepoMind.Scanner.Parsers;
using Xunit;

namespace RepoMind.Scanner.Tests.Parsers;

public class ConfigParserTests
{
    [Fact]
    public void ScanProject_ExtractsKeysFromAppSettingsJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var json = @"{
  ""ConnectionStrings"": {
    ""Default"": ""Server=localhost""
  },
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information""
    }
  }
}";
            File.WriteAllText(Path.Combine(tempDir, "appsettings.json"), json);

            var entries = ConfigParser.ScanProject(tempDir);

            entries.Should().Contain(e => e.KeyName == "ConnectionStrings:Default" && e.Source == "appsettings");
            entries.Should().Contain(e => e.KeyName == "Logging:LogLevel:Default" && e.Source == "appsettings");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanProject_DetectsCSharpConfigPatterns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var csContent = @"
public class MyService
{
    public void Init()
    {
        var connStr = Environment.GetEnvironmentVariable(""DB_CONNECTION"");
        var apiKey = Configuration[""ApiSettings:Key""];
        var section = config.GetSection(""Features"");
    }
}";
            File.WriteAllText(Path.Combine(tempDir, "MyService.cs"), csContent);

            var entries = ConfigParser.ScanProject(tempDir);

            entries.Should().Contain(e => e.KeyName == "DB_CONNECTION" && e.Source == "env_var");
            entries.Should().Contain(e => e.KeyName == "ApiSettings:Key" && e.Source == "IConfiguration");
            entries.Should().Contain(e => e.KeyName == "Features" && e.Source == "IConfiguration");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanProject_DeduplicatesEntries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var csContent = @"
public class ServiceA
{
    public void A() { var x = Environment.GetEnvironmentVariable(""MY_KEY""); }
    public void B() { var y = Environment.GetEnvironmentVariable(""MY_KEY""); }
}";
            File.WriteAllText(Path.Combine(tempDir, "ServiceA.cs"), csContent);

            var entries = ConfigParser.ScanProject(tempDir);

            entries.Where(e => e.KeyName == "MY_KEY").Should().HaveCount(1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
