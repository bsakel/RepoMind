namespace RepoMind.Scanner.Models;

public record ProjectInfo(
    string Name,
    string DirectoryPath,
    string? SolutionFile,
    string? GitRemoteUrl,
    string? DefaultBranch);
