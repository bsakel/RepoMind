namespace RepoMind.Scanner.Models;

public record MethodInfo(
    string MethodName,
    string ReturnType,
    bool IsPublic,
    bool IsStatic,
    List<MethodParameter> Parameters,
    List<EndpointInfo> Endpoints);

public record MethodParameter(string Name, string Type, int Position);

public record EndpointInfo(
    string HttpMethod,    // GET, POST, PUT, DELETE, QUERY, MUTATION, SUBSCRIPTION
    string RouteTemplate, // "/api/content/{id}" or GraphQL operation name
    string Kind);         // REST or GraphQL
