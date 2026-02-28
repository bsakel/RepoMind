namespace RepoMind.Scanner.Models;

public record TypeInfo(
    string NamespaceName,
    string TypeName,
    string Kind,       // class, interface, enum, record, struct
    bool IsPublic,
    string FilePath,
    string? BaseType,
    List<string> ImplementedInterfaces,
    List<string> InjectedDependencies,
    string? SummaryComment,
    List<MethodInfo>? Methods = null,
    bool IsPartial = false);
