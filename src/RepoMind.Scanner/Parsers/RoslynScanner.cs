using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using EndpointInfo = RepoMind.Scanner.Models.EndpointInfo;
using MethodInfo = RepoMind.Scanner.Models.MethodInfo;
using MethodParameter = RepoMind.Scanner.Models.MethodParameter;
using TypeInfo = RepoMind.Scanner.Models.TypeInfo;

namespace RepoMind.Scanner.Parsers;

public static class RoslynScanner
{
    public static List<TypeInfo> ScanSourceFiles(string projectDir)
    {
        var results = new List<TypeInfo>();

        var srcDirs = FindSrcDirectories(projectDir);
        var csFiles = srcDirs.SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToList();

        Log.Information("  Scanning {Count} source files...", csFiles.Count);

        foreach (var file in csFiles)
        {
            try
            {
                var types = ParseFile(file, projectDir);
                results.AddRange(types);
            }
            catch (Exception ex)
            {
                Log.Warning("  Failed to parse {File}: {Error}",
                    Path.GetRelativePath(projectDir, file), ex.Message);
            }
        }

        return results;
    }

    private static List<string> FindSrcDirectories(string projectDir)
    {
        var srcDir = Path.Combine(projectDir, "src");
        if (Directory.Exists(srcDir))
            return [srcDir];
        return [projectDir];
    }

    private static List<TypeInfo> ParseFile(string filePath, string projectDir)
    {
        var code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var results = new List<TypeInfo>();
        var relativePath = Path.GetRelativePath(projectDir, filePath);

        var fileScopedNs = root.DescendantNodes()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        var blockNamespaces = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .ToList();

        if (fileScopedNs != null)
        {
            results.AddRange(ExtractTypes(fileScopedNs.Members, fileScopedNs.Name.ToString(), relativePath));
        }

        foreach (var ns in blockNamespaces)
        {
            var nsName = BuildFullNamespaceName(ns);
            results.AddRange(ExtractTypes(ns.Members, nsName, relativePath));
        }

        var topLevelTypes = root.ChildNodes()
            .Where(n => n is TypeDeclarationSyntax or EnumDeclarationSyntax);
        foreach (var t in topLevelTypes)
        {
            var typeInfo = ExtractSingleType(t, "<global>", relativePath);
            if (typeInfo != null) results.Add(typeInfo);
        }

        return results;
    }

    private static string BuildFullNamespaceName(NamespaceDeclarationSyntax ns)
    {
        var parts = new List<string> { ns.Name.ToString() };
        var current = ns.Parent;
        while (current is NamespaceDeclarationSyntax parentNs)
        {
            parts.Insert(0, parentNs.Name.ToString());
            current = parentNs.Parent;
        }
        return string.Join(".", parts);
    }

    private static List<TypeInfo> ExtractTypes(
        SyntaxList<MemberDeclarationSyntax> members, string namespaceName, string filePath)
    {
        var results = new List<TypeInfo>();
        foreach (var member in members)
        {
            var typeInfo = ExtractSingleType(member, namespaceName, filePath);
            if (typeInfo != null) results.Add(typeInfo);
        }
        return results;
    }

    private static TypeInfo? ExtractSingleType(SyntaxNode node, string namespaceName, string filePath)
    {
        string? typeName = null;
        string kind = "unknown";
        bool isPublic = false;
        bool isPartial = false;
        string? baseType = null;
        var interfaces = new List<string>();
        var injectedDeps = new List<string>();
        string? summary = null;

        switch (node)
        {
            case ClassDeclarationSyntax cls:
                typeName = cls.Identifier.Text;
                kind = "class";
                isPublic = cls.Modifiers.Any(SyntaxKind.PublicKeyword);
                isPartial = cls.Modifiers.Any(SyntaxKind.PartialKeyword);
                ExtractBaseTypes(cls.BaseList, out baseType, out interfaces);
                injectedDeps = ExtractConstructorInjections(cls);
                summary = ExtractXmlSummary(cls);
                break;

            case InterfaceDeclarationSyntax iface:
                typeName = iface.Identifier.Text;
                kind = "interface";
                isPublic = iface.Modifiers.Any(SyntaxKind.PublicKeyword);
                isPartial = iface.Modifiers.Any(SyntaxKind.PartialKeyword);
                ExtractBaseTypes(iface.BaseList, out baseType, out interfaces);
                summary = ExtractXmlSummary(iface);
                break;

            case RecordDeclarationSyntax rec:
                typeName = rec.Identifier.Text;
                kind = rec.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record";
                isPublic = rec.Modifiers.Any(SyntaxKind.PublicKeyword);
                isPartial = rec.Modifiers.Any(SyntaxKind.PartialKeyword);
                ExtractBaseTypes(rec.BaseList, out baseType, out interfaces);
                injectedDeps = ExtractConstructorInjections(rec);
                summary = ExtractXmlSummary(rec);
                break;

            case StructDeclarationSyntax str:
                typeName = str.Identifier.Text;
                kind = "struct";
                isPublic = str.Modifiers.Any(SyntaxKind.PublicKeyword);
                isPartial = str.Modifiers.Any(SyntaxKind.PartialKeyword);
                ExtractBaseTypes(str.BaseList, out baseType, out interfaces);
                summary = ExtractXmlSummary(str);
                break;

            case EnumDeclarationSyntax enm:
                typeName = enm.Identifier.Text;
                kind = "enum";
                isPublic = enm.Modifiers.Any(SyntaxKind.PublicKeyword);
                summary = ExtractXmlSummary(enm);
                break;
        }

        if (typeName == null) return null;

        // Extract public methods with endpoints for classes
        List<MethodInfo>? methods = null;
        if (node is TypeDeclarationSyntax typeDeclaration && isPublic)
        {
            methods = ExtractMethods(typeDeclaration);
        }

        return new TypeInfo(
            namespaceName, typeName, kind, isPublic,
            filePath, baseType, interfaces, injectedDeps, summary, methods, isPartial);
    }

    private static void ExtractBaseTypes(
        BaseListSyntax? baseList, out string? baseType, out List<string> interfaces)
    {
        baseType = null;
        interfaces = new List<string>();
        if (baseList == null) return;

        var types = baseList.Types;
        for (int i = 0; i < types.Count; i++)
        {
            var name = types[i].Type.ToString();
            if (i == 0)
            {
                // First item: could be base class or interface.
                // Only treat as interface if it matches I+uppercase heuristic.
                if (name.Length >= 2 && name[0] == 'I' && char.IsUpper(name[1]))
                    interfaces.Add(name);
                else
                    baseType = name;
            }
            else
            {
                // Subsequent items are always interfaces in C#.
                interfaces.Add(name);
            }
        }
    }

    private static List<string> ExtractConstructorInjections(TypeDeclarationSyntax typeDecl)
    {
        var deps = new List<string>();

        if (typeDecl.ParameterList != null)
        {
            foreach (var param in typeDecl.ParameterList.Parameters)
            {
                var typeName = param.Type?.ToString();
                if (typeName != null && IsLikelyDependency(typeName))
                    deps.Add(typeName);
            }
        }

        var constructors = typeDecl.Members.OfType<ConstructorDeclarationSyntax>();
        foreach (var ctor in constructors)
        {
            foreach (var param in ctor.ParameterList.Parameters)
            {
                var typeName = param.Type?.ToString();
                if (typeName != null && IsLikelyDependency(typeName))
                    deps.Add(typeName);
            }
        }

        return deps.Distinct().ToList();
    }

    private static bool IsLikelyDependency(string typeName)
    {
        var skip = new HashSet<string> { "string", "int", "bool", "long", "double",
            "float", "decimal", "Guid", "DateTime", "TimeSpan", "CancellationToken" };
        var baseName = typeName.TrimEnd('?');
        if (skip.Contains(baseName)) return false;

        if (baseName.Length >= 2 && baseName[0] == 'I' && char.IsUpper(baseName[1]))
            return true;

        if (baseName.StartsWith("IOptions<") || baseName.StartsWith("ILogger<"))
            return true;

        return false;
    }

    private static string? ExtractXmlSummary(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                     || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            .FirstOrDefault();

        if (trivia == default) return null;

        var xml = trivia.ToString();
        var start = xml.IndexOf("<summary>");
        var end = xml.IndexOf("</summary>");
        if (start >= 0 && end > start)
        {
            var rawContent = xml[(start + 9)..end];
            var content = string.Join("\n", rawContent
                .Split('\n')
                .Select(line => line.TrimStart().TrimStart('/').TrimStart()))
                .Trim();
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        return null;
    }

    private static List<MethodInfo> ExtractMethods(TypeDeclarationSyntax typeDecl)
    {
        var methods = new List<MethodInfo>();
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            var isPublic = method.Modifiers.Any(SyntaxKind.PublicKeyword);
            if (!isPublic) continue;

            var isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);
            var returnType = method.ReturnType.ToString();
            var methodName = method.Identifier.Text;

            var parameters = method.ParameterList.Parameters
                .Select((p, i) => new MethodParameter(
                    p.Identifier.Text,
                    p.Type?.ToString() ?? "unknown",
                    i))
                .ToList();

            var endpoints = ExtractEndpoints(method);

            methods.Add(new MethodInfo(methodName, returnType, isPublic, isStatic, parameters, endpoints));
        }
        return methods;
    }

    private static readonly Dictionary<string, string> HttpAttributeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HttpGet"] = "GET", ["HttpPost"] = "POST", ["HttpPut"] = "PUT",
        ["HttpDelete"] = "DELETE", ["HttpPatch"] = "PATCH", ["HttpHead"] = "HEAD",
        ["HttpOptions"] = "OPTIONS"
    };

    private static readonly HashSet<string> GraphQLAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Query", "Mutation", "Subscription", "ExtendObjectType",
        "QueryType", "MutationType", "SubscriptionType"
    };

    private static string ExtractRouteTemplate(AttributeSyntax attr)
    {
        var firstArg = attr.ArgumentList?.Arguments.FirstOrDefault();
        if (firstArg == null) return "";

        if (firstArg.Expression is LiteralExpressionSyntax literal)
            return literal.Token.ValueText;

        return firstArg.ToString().Trim('"');
    }

    private static string GetClassRoutePrefix(MethodDeclarationSyntax method)
    {
        if (method.Parent is not TypeDeclarationSyntax typeDecl)
            return "";

        foreach (var attrList in typeDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name.EndsWith("Attribute"))
                    name = name.Substring(0, name.Length - "Attribute".Length);

                if (name is "Route")
                    return ExtractRouteTemplate(attr);
            }
        }
        return "";
    }

    private static List<EndpointInfo> ExtractEndpoints(MethodDeclarationSyntax method)
    {
        var endpoints = new List<EndpointInfo>();
        var classRoutePrefix = GetClassRoutePrefix(method);

        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = attr.Name.ToString();
                if (attrName.EndsWith("Attribute"))
                    attrName = attrName.Substring(0, attrName.Length - "Attribute".Length);

                // REST endpoints
                if (HttpAttributeMap.TryGetValue(attrName, out var httpMethod))
                {
                    var methodRoute = ExtractRouteTemplate(attr);
                    var route = CombineRouteParts(classRoutePrefix, methodRoute);
                    endpoints.Add(new EndpointInfo(httpMethod, route, "REST"));
                }

                // GraphQL endpoints
                if (GraphQLAttributes.Contains(attrName))
                {
                    var kind = attrName.Contains("Mutation", StringComparison.OrdinalIgnoreCase) ? "MUTATION"
                        : attrName.Contains("Subscription", StringComparison.OrdinalIgnoreCase) ? "SUBSCRIPTION"
                        : "QUERY";
                    var opName = ExtractRouteTemplate(attr) is { Length: > 0 } name
                        ? name : method.Identifier.Text;
                    endpoints.Add(new EndpointInfo(kind, opName, "GraphQL"));
                }
            }
        }

        return endpoints;
    }

    private static string CombineRouteParts(string prefix, string methodRoute)
    {
        if (string.IsNullOrEmpty(prefix)) return methodRoute;
        if (string.IsNullOrEmpty(methodRoute)) return prefix;
        return prefix.TrimEnd('/') + "/" + methodRoute.TrimStart('/');
    }
}
