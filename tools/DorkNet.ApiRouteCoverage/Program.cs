using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

var serverAssembly = LoadServerAssembly(args);
var serverAssemblyWriteTime = File.GetLastWriteTimeUtc(serverAssembly.Location);
Console.WriteLine($"Using server assembly: {serverAssembly.Location}");
Console.WriteLine($"Server assembly timestamp (UTC): {serverAssemblyWriteTime:O}");

var routes = DiscoverRoutes(serverAssembly).OrderBy(r => r.Method).ThenBy(r => r.Template).ToArray();
var failures = new List<string>();

if (routes.Length == 0)
    failures.Add("No HTTP controller routes were discovered.");

foreach (var route in routes)
{
    if (string.IsNullOrWhiteSpace(route.Template))
        failures.Add($"{route.Source}: empty route template");

    if (route.SamplePath.Contains('{') || route.SamplePath.Contains('}'))
        failures.Add($"{route.Source}: unresolved route parameter in {route.SamplePath}");

    if (route.SamplePath.Contains('[') || route.SamplePath.Contains(']'))
        failures.Add($"{route.Source}: unresolved token in {route.SamplePath}");

    if (!route.SamplePath.StartsWith('/'))
        failures.Add($"{route.Source}: sample path must be absolute: {route.SamplePath}");
}

Console.WriteLine($"Discovered {routes.Length} HTTP route test cases.");
foreach (var route in routes)
    Console.WriteLine($"{route.Method,-6} {route.SamplePath,-72} {route.Source}");

if (failures.Count == 0)
    return 0;

Console.Error.WriteLine();
Console.Error.WriteLine("Route coverage failed:");
foreach (var failure in failures)
    Console.Error.WriteLine($"- {failure}");
return 1;

static Assembly LoadServerAssembly(string[] args)
{
    if (args.Length > 0)
        return LoadAssemblyFromPath(Path.GetFullPath(args[0]));

    var repositoryRoot = FindRepositoryRoot();
    var candidates = new[]
    {
        Path.Combine(repositoryRoot, "DorkNet.Server", "bin", "Debug", "net10.0", "DorkNet.Server.dll"),
        Path.Combine(repositoryRoot, "DorkNet.Server", "bin", "Release", "net10.0", "DorkNet.Server.dll")
    };

    var existingCandidates = candidates.Where(File.Exists).ToArray();
    if (existingCandidates.Length == 0)
    {
        throw new FileNotFoundException(
            "Could not find a built DorkNet.Server assembly. Build DorkNet.Server first or pass the DLL path as the first argument.",
            string.Join(Environment.NewLine, candidates));
    }

    if (existingCandidates.Length > 1)
    {
        throw new InvalidOperationException(
            "Multiple DorkNet.Server assemblies were found. Pass the DLL path as the first argument so route coverage validates the intended build."
            + Environment.NewLine
            + string.Join(Environment.NewLine, existingCandidates));
    }

    return LoadAssemblyFromPath(existingCandidates[0]);
}

static Assembly LoadAssemblyFromPath(string assemblyPath)
{
    var assemblyDirectory = Path.GetDirectoryName(assemblyPath)
        ?? throw new DirectoryNotFoundException($"Could not resolve assembly directory for {assemblyPath}.");

    AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
    {
        var dependencyPath = Path.Combine(assemblyDirectory, assemblyName.Name + ".dll");
        return File.Exists(dependencyPath)
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependencyPath)
            : null;
    };

    return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "DorkNet.Server", "DorkNet.Server.csproj")))
            return current.FullName;

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the repository root from the route coverage tool output path.");
}

static IEnumerable<RouteCase> DiscoverRoutes(Assembly serverAssembly)
{
    var controllerBaseType = typeof(ControllerBase);
    foreach (var controller in serverAssembly.GetTypes()
        .Where(t => !t.IsAbstract && controllerBaseType.IsAssignableFrom(t)))
    {
        var controllerName = ControllerName(controller);
        var controllerRouteTemplates = controller.GetCustomAttributes(inherit: true)
            .OfType<IRouteTemplateProvider>()
            .Select(a => a.Template)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .DefaultIfEmpty("")
            .ToArray();

        foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            var httpAttrs = method.GetCustomAttributes(inherit: true)
                .OfType<HttpMethodAttribute>()
                .ToArray();
            if (httpAttrs.Length == 0)
                continue;

            foreach (var attr in httpAttrs)
            {
                var attrMethods = attr.HttpMethods.Any()
                    ? attr.HttpMethods
                    : new[] { "ANY" };
                var actionName = method.Name;
                foreach (var httpMethod in attrMethods)
                {
                    foreach (var controllerTemplate in controllerRouteTemplates)
                    {
                        var template = CombineTemplates(controllerTemplate, attr.Template);
                        template = ReplaceRouteTokens(template, controllerName, actionName);
                        yield return new RouteCase(
                            httpMethod.ToUpperInvariant(),
                            template,
                            ToSamplePath(template),
                            $"{controller.FullName}.{method.Name}");
                    }
                }
            }
        }
    }
}

static string ControllerName(Type controller)
{
    const string suffix = "Controller";
    return controller.Name.EndsWith(suffix, StringComparison.Ordinal)
        ? controller.Name[..^suffix.Length]
        : controller.Name;
}

static string CombineTemplates(string? controllerTemplate, string? actionTemplate)
{
    controllerTemplate ??= "";
    actionTemplate ??= "";

    if (actionTemplate.StartsWith("~/", StringComparison.Ordinal))
        return "/" + actionTemplate[2..].TrimStart('/');

    if (actionTemplate.StartsWith("/", StringComparison.Ordinal))
        return "/" + actionTemplate.TrimStart('/');

    var left = controllerTemplate.Trim('/');
    var right = actionTemplate.Trim('/');

    if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
        return "/";
    if (string.IsNullOrEmpty(left))
        return "/" + right;
    if (string.IsNullOrEmpty(right))
        return "/" + left;
    return "/" + left + "/" + right;
}

static string ReplaceRouteTokens(string template, string controllerName, string actionName)
{
    return template
        .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
        .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
}

static string ToSamplePath(string template)
{
    var path = RouteParameterRegex().Replace(template, match =>
    {
        var constraint = match.Groups["constraint"].Value.ToLowerInvariant();
        if (constraint.Contains("long") || constraint.Contains("int"))
            return "1";
        if (constraint.Contains("bool"))
            return "true";
        if (constraint.Contains("guid"))
            return "00000000-0000-0000-0000-000000000001";
        return "sample";
    });

    path = Regex.Replace(path, "/+", "/");
    return path.StartsWith('/') ? path : "/" + path;
}

internal sealed record RouteCase(string Method, string Template, string SamplePath, string Source);

partial class Program
{
    [GeneratedRegex(@"\{[*]?(?<name>[^}:=\?]+)(?<constraint>:[^}=\?]+)?(?<optional>\?)?(?<default>=[^}]+)?\}")]
    private static partial Regex RouteParameterRegex();
}
