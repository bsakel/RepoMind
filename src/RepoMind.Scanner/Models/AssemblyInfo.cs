namespace RepoMind.Scanner.Models;

public record AssemblyInfo(
    string ProjectName,
    string CsprojPath,
    string AssemblyName,
    string? TargetFramework,
    string? OutputType,
    bool IsTest,
    List<PackageRef> PackageReferences,
    List<string> ProjectReferences);

public record PackageRef(string Name, string? Version, bool IsInternal);
