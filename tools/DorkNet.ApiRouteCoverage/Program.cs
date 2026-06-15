using System.Reflection;
using System.Text.RegularExpressions;
using DorkNet.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

var routes = DiscoverRoutes().OrderBy(r => r.Method).ThenBy(r => r.Template).ToArray();
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

Console.WriteLine($"Discovered {routes.Length} HTTP route cases.");
foreach (var route in routes)
    Console.WriteLine($"{route.Method,-6} {route.SamplePath,-72} {route.Source}");

if (failures.Count == 0)
    return 0;

Console.Error.WriteLine();
Console.Error.WriteLine("Route coverage failed:");
foreach (var failure in failures)
    Console.Error.WriteLine($"- {failure}");
return 1;

static IEnumerable<RouteCase> DiscoverRoutes()
{
    var controllerBaseType = typeof(ControllerBase);
    var serverAssembly = typeof(DomainConfig).Assembly;

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
            if (httpAttrs.Length == 0) continue;

            foreach (var attr in httpAttrs)
            {
                IEnumerable<string> attrMethods = attr.HttpMethods.Any()
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

    if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right)) return "/";
    if (string.IsNullOrEmpty(left)) return "/" + right;
    if (string.IsNullOrEmpty(right)) return "/" + left;
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
        if (constraint.Contains("long") || constraint.Contains("int")) return "1";
        if (constraint.Contains("bool")) return "true";
        if (constraint.Contains("guid")) return "00000000-0000-0000-0000-000000000001";
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
