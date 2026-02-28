namespace RepoMind.Mcp.Configuration;

public class RepoMindConfiguration
{
    public string RootPath { get; set; } = string.Empty;
    public string DbPath { get; set; } = string.Empty;
    public int MaxParallelism { get; set; } = 4;
    public List<string> AllowedBranches { get; set; } = ["master", "main"];
}
