using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace DorkNet.Server.Tests;

public static partial class EndpointContractDiscovery
{
    public static IReadOnlyList<EndpointContract> Discover()
    {
        var serverAssembly = typeof(Program).Assembly;
        var controllerBaseType = typeof(ControllerBase);
        return serverAssembly.GetTypes()
            .Where(t => !t.IsAbstract && controllerBaseType.IsAssignableFrom(t))
            .SelectMany(DiscoverControllerRoutes)
            .OrderBy(route => route.Method, StringComparer.Ordinal)
            .ThenBy(route => route.SamplePath, StringComparer.Ordinal)
            .ThenBy(route => route.Source, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<EndpointContract> DiscoverControllerRoutes(Type controller)
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
            {
                continue;
            }

            foreach (var attr in httpAttrs)
            {
                var attrMethods = attr.HttpMethods.Any()
                    ? attr.HttpMethods
                    : new[] { "ANY" };

                foreach (var httpMethod in attrMethods)
                {
                    foreach (var controllerTemplate in controllerRouteTemplates)
                    {
                        var template = CombineTemplates(controllerTemplate, attr.Template);
                        template = ReplaceRouteTokens(template, controllerName, method.Name);
                        yield return new EndpointContract(
                            httpMethod.ToUpperInvariant(),
                            template,
                            ToSamplePath(template),
                            $"{controller.FullName}.{method.Name}",
                            RequiresAuthorization(controller, method),
                            IsAdminEndpoint(controller, method),
                            ConsumesContentTypes(controller, method));
                    }
                }
            }
        }
    }

    private static bool RequiresAuthorization(Type controller, MethodInfo method)
    {
        var attributes = controller.GetCustomAttributes(inherit: true)
            .Concat(method.GetCustomAttributes(inherit: true))
            .ToArray();

        if (attributes.OfType<IAllowAnonymous>().Any())
        {
            return false;
        }

        return attributes.OfType<IAuthorizeData>().Any();
    }

    private static bool IsAdminEndpoint(Type controller, MethodInfo method)
    {
        return controller.FullName?.Contains(".Controllers.Admin.", StringComparison.Ordinal) == true
            || method.Name.Contains("Admin", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ConsumesContentTypes(Type controller, MethodInfo method)
    {
        var consumes = method.GetCustomAttributes(inherit: true)
            .Concat(controller.GetCustomAttributes(inherit: true))
            .OfType<ConsumesAttribute>()
            .SelectMany(a => a.ContentTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return consumes.Length == 0
            ? ["application/json"]
            : consumes;
    }

    private static string ControllerName(Type controller)
    {
        const string suffix = "Controller";
        return controller.Name.EndsWith(suffix, StringComparison.Ordinal)
            ? controller.Name[..^suffix.Length]
            : controller.Name;
    }

    private static string CombineTemplates(string? controllerTemplate, string? actionTemplate)
    {
        controllerTemplate ??= "";
        actionTemplate ??= "";

        if (actionTemplate.StartsWith("~/", StringComparison.Ordinal))
        {
            return "/" + actionTemplate[2..].TrimStart('/');
        }

        if (actionTemplate.StartsWith("/", StringComparison.Ordinal))
        {
            return "/" + actionTemplate.TrimStart('/');
        }

        var left = controllerTemplate.Trim('/');
        var right = actionTemplate.Trim('/');

        if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
        {
            return "/";
        }

        if (string.IsNullOrEmpty(left))
        {
            return "/" + right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return "/" + left;
        }

        return "/" + left + "/" + right;
    }

    private static string ReplaceRouteTokens(string template, string controllerName, string actionName)
    {
        return template
            .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToSamplePath(string template)
    {
        var path = RouteParameterRegex().Replace(template, match =>
        {
            var name = match.Groups["name"].Value.ToLowerInvariant();
            var constraint = match.Groups["constraint"].Value.ToLowerInvariant();

            if (name.Contains("playerid") || name.Contains("accountid") || name == "id")
            {
                return EndpointContract.TestPlayerToken;
            }

            if (constraint.Contains("long") || constraint.Contains("int") || name.EndsWith("id"))
            {
                return "1";
            }

            if (constraint.Contains("bool"))
            {
                return "true";
            }

            if (constraint.Contains("guid"))
            {
                return "00000000-0000-0000-0000-000000000001";
            }

            if (name.Contains("roomname"))
            {
                return "DormRoom";
            }

            if (name.Contains("locale"))
            {
                return "en";
            }

            return "sample";
        });

        path = Regex.Replace(path, "/+", "/");
        return path.StartsWith('/') ? path : "/" + path;
    }

    [GeneratedRegex(@"\{[*]?(?<name>[^}:=\?]+)(?<constraint>:[^}=\?]+)?(?<optional>\?)?(?<default>=[^}]+)?\}")]
    private static partial Regex RouteParameterRegex();
}

public sealed record EndpointContract(
    string Method,
    string Template,
    string SamplePath,
    string Source,
    bool RequiresAuthorization,
    bool IsAdminEndpoint,
    string[] Consumes)
{
    public const string TestPlayerToken = "__test_player_id__";

    public string PathFor(GameClientSession session)
    {
        return SamplePath.Replace(
            TestPlayerToken,
            session.PlayerId.ToString(),
            StringComparison.Ordinal);
    }
}
