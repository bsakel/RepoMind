namespace RepoMind.Mcp.Configuration;

public class RepoMindConfiguration
{
    public required string RootPath { get; set; }
    public required string DbPath { get; set; }
    public int MaxParallelism { get; set; } = 4;
}
