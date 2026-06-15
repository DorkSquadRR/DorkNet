using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

var options = EndpointContractOptions.Parse(args);
var serverAssembly = LoadServerAssembly(options.ServerAssemblyPath);
var routes = DiscoverRoutes(serverAssembly).OrderBy(r => r.Method).ThenBy(r => r.SamplePath).ToArray();
var results = new List<EndpointProbeResult>();
var failures = new List<string>();

ValidateRouteShapes(routes, failures);

Console.WriteLine($"Using server assembly: {serverAssembly.Location}");
Console.WriteLine($"Server assembly timestamp (UTC): {File.GetLastWriteTimeUtc(serverAssembly.Location):O}");
Console.WriteLine($"Discovered {routes.Length} HTTP endpoint contracts.");

if (options.BaseUrl is { } baseUrl)
{
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

    foreach (var route in routes)
    {
        var result = await ProbeAsync(http, route, options);
        results.Add(result);
        if (!result.Passed)
            failures.Add($"{route.Source}: {route.Method} {route.SamplePath} {result.Expected} but got {result.Observed}");
    }
}
else
{
    foreach (var route in routes)
    {
        results.Add(new EndpointProbeResult(
            route.Method,
            route.SamplePath,
            route.Source,
            route.RequiresAuthorization,
            "route-shape",
            null,
            null,
            true));
    }
}

if (options.ReportPath is { } reportPath)
    WriteReport(reportPath, serverAssembly, routes.Length, results, failures);

foreach (var result in results)
{
    var status = result.StatusCode is int code ? code.ToString() : "-";
    Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL"),-4} {result.Method,-6} {status,-3} {result.Path,-72} {result.Source}");
}

if (failures.Count == 0)
    return 0;

Console.Error.WriteLine();
Console.Error.WriteLine("Endpoint contract failed:");
foreach (var failure in failures)
    Console.Error.WriteLine($"- {failure}");
return 1;

static void ValidateRouteShapes(IEnumerable<EndpointContract> routes, List<string> failures)
{
    foreach (var route in routes)
    {
        if (string.IsNullOrWhiteSpace(route.Template))
            failures.Add($"{route.Source}: empty route template");

        if (route.SamplePath.Contains('{') || route.SamplePath.Contains('}'))
            failures.Add($"{route.Source}: unresolved route parameter in {route.SamplePath}");

        if (route.SamplePath.Contains('[') || route.SamplePath.Contains(']'))
            failures.Add($"{route.Source}: unresolved route token in {route.SamplePath}");

        if (!route.SamplePath.StartsWith('/'))
            failures.Add($"{route.Source}: sample path must be absolute: {route.SamplePath}");
    }
}

static async Task<EndpointProbeResult> ProbeAsync(
    HttpClient http,
    EndpointContract route,
    EndpointContractOptions options)
{
    using var request = new HttpRequestMessage(ToHttpMethod(route.Method), route.SamplePath.TrimStart('/'));
    request.Headers.Accept.ParseAdd("application/json");
    request.Headers.TryAddWithoutValidation("X-DorkNet-Version", options.VersionHeader);
    request.Headers.UserAgent.ParseAdd("DorkNetEndpointContract/1.0");

    if (NeedsRequestBody(request.Method))
        request.Content = CreateContent(route);

    try
    {
        using var response = await http.SendAsync(request);
        var statusCode = (int)response.StatusCode;
        var passed = ResponseMatchesContract(route, response.StatusCode);
        return new EndpointProbeResult(
            route.Method,
            route.SamplePath,
            route.Source,
            route.RequiresAuthorization,
            ExpectedContract(route),
            statusCode,
            null,
            passed);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return new EndpointProbeResult(
            route.Method,
            route.SamplePath,
            route.Source,
            route.RequiresAuthorization,
            ExpectedContract(route),
            null,
            ex.Message,
            false);
    }
}

static bool ResponseMatchesContract(EndpointContract route, HttpStatusCode statusCode)
{
    if (route.RequiresAuthorization)
        return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    return (int)statusCode < 500
        && statusCode != HttpStatusCode.MethodNotAllowed;
}

static string ExpectedContract(EndpointContract route)
{
    return route.RequiresAuthorization
        ? "unauthenticated protected endpoint should return 401 or 403"
        : "public endpoint should return a non-5xx, non-405 response";
}

static HttpMethod ToHttpMethod(string method)
{
    return string.Equals(method, "ANY", StringComparison.OrdinalIgnoreCase)
        ? HttpMethod.Get
        : new HttpMethod(method);
}

static bool NeedsRequestBody(HttpMethod method)
{
    return method == HttpMethod.Post
        || method == HttpMethod.Put
        || method.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);
}

static HttpContent CreateContent(EndpointContract route)
{
    var consumes = route.Consumes.Select(c => c.ToLowerInvariant()).ToArray();
    if (consumes.Any(c => c.Contains("application/x-www-form-urlencoded")))
    {
        return new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "endpoint-contract",
            ["password"] = "endpoint-contract",
            ["device_id"] = "endpoint-contract-device",
            ["platform"] = "0",
            ["platform_id"] = "endpoint-contract-platform",
        });
    }

    if (consumes.Any(c => c.Contains("multipart/form-data")))
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("endpoint-contract"), "name");
        content.Add(new ByteArrayContent([]), "file", "endpoint-contract.bin");
        return content;
    }

    if (consumes.Any(c => c.Contains("text/plain")))
        return new StringContent("", Encoding.UTF8, "text/plain");

    return new StringContent("{}", Encoding.UTF8, "application/json");
}

static IEnumerable<EndpointContract> DiscoverRoutes(Assembly serverAssembly)
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
                            ConsumesContentTypes(controller, method));
                    }
                }
            }
        }
    }
}

static bool RequiresAuthorization(Type controller, MethodInfo method)
{
    var attributes = controller.GetCustomAttributes(inherit: true)
        .Concat(method.GetCustomAttributes(inherit: true))
        .ToArray();

    if (attributes.OfType<IAllowAnonymous>().Any())
        return false;

    return attributes.OfType<IAuthorizeData>().Any();
}

static string[] ConsumesContentTypes(Type controller, MethodInfo method)
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
        var name = match.Groups["name"].Value.ToLowerInvariant();
        var constraint = match.Groups["constraint"].Value.ToLowerInvariant();

        if (constraint.Contains("long") || constraint.Contains("int") || name.EndsWith("id"))
            return "1";
        if (constraint.Contains("bool"))
            return "true";
        if (constraint.Contains("guid"))
            return "00000000-0000-0000-0000-000000000001";
        if (name.Contains("roomname"))
            return "DormRoom";
        if (name.Contains("locale"))
            return "en";
        return "sample";
    });

    path = Regex.Replace(path, "/+", "/");
    return path.StartsWith('/') ? path : "/" + path;
}

static Assembly LoadServerAssembly(string? configuredPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
        return LoadAssemblyFromPath(Path.GetFullPath(configuredPath));

    var repositoryRoot = FindRepositoryRoot();
    var candidates = new[]
    {
        Path.Combine(repositoryRoot, "DorkNet.Server", "bin", "Debug", "net9.0", "DorkNet.Server.dll"),
        Path.Combine(repositoryRoot, "DorkNet.Server", "bin", "Release", "net9.0", "DorkNet.Server.dll")
    };
    var existingCandidates = candidates.Where(File.Exists).ToArray();
    if (existingCandidates.Length == 0)
    {
        throw new FileNotFoundException(
            "Could not find a built DorkNet.Server assembly. Build DorkNet.Server first or pass --server-assembly.",
            string.Join(Environment.NewLine, candidates));
    }

    if (existingCandidates.Length > 1)
    {
        throw new InvalidOperationException(
            "Multiple DorkNet.Server assemblies were found. Pass --server-assembly so endpoint checks validate the intended build."
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

    throw new DirectoryNotFoundException("Could not locate the repository root from the endpoint contract tool output path.");
}

static void WriteReport(
    string reportPath,
    Assembly serverAssembly,
    int routeCount,
    IReadOnlyCollection<EndpointProbeResult> results,
    IReadOnlyCollection<string> failures)
{
    var report = new EndpointContractReport(
        serverAssembly.Location,
        File.GetLastWriteTimeUtc(serverAssembly.Location),
        routeCount,
        results.Count(r => r.Passed),
        results.Count(r => !r.Passed),
        failures,
        results);

    var fullPath = Path.GetFullPath(reportPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

internal sealed record EndpointContract(
    string Method,
    string Template,
    string SamplePath,
    string Source,
    bool RequiresAuthorization,
    string[] Consumes);

internal sealed record EndpointProbeResult(
    string Method,
    string Path,
    string Source,
    bool RequiresAuthorization,
    string Expected,
    int? StatusCode,
    string? Error,
    bool Passed)
{
    public string Observed => StatusCode is int code ? code.ToString() : Error ?? "no response";
}

internal sealed record EndpointContractReport(
    string ServerAssembly,
    DateTime ServerAssemblyTimestampUtc,
    int RouteCount,
    int Passed,
    int Failed,
    IReadOnlyCollection<string> Failures,
    IReadOnlyCollection<EndpointProbeResult> Results);

internal sealed class EndpointContractOptions
{
    public string? ServerAssemblyPath { get; private init; }
    public string? BaseUrl { get; private init; }
    public string? ReportPath { get; private init; }
    public string VersionHeader { get; private init; } = "december_2020_12_18";
    public int TimeoutSeconds { get; private init; } = 10;

    public static EndpointContractOptions Parse(string[] args)
    {
        string? serverAssemblyPath = null;
        string? baseUrl = null;
        string? reportPath = null;
        var versionHeader = "december_2020_12_18";
        var timeoutSeconds = 10;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--server-assembly":
                    serverAssemblyPath = NextValue(args, ref i, arg);
                    break;
                case "--base-url":
                    baseUrl = NextValue(args, ref i, arg);
                    break;
                case "--report":
                    reportPath = NextValue(args, ref i, arg);
                    break;
                case "--version":
                    versionHeader = NextValue(args, ref i, arg);
                    break;
                case "--timeout-seconds":
                    timeoutSeconds = int.Parse(NextValue(args, ref i, arg));
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: DorkNet.EndpointContract [--server-assembly PATH] [--base-url URL] [--report PATH]");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new EndpointContractOptions
        {
            ServerAssemblyPath = serverAssemblyPath,
            BaseUrl = baseUrl,
            ReportPath = reportPath,
            VersionHeader = versionHeader,
            TimeoutSeconds = timeoutSeconds,
        };
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{option} requires a value.");

        index++;
        return args[index];
    }
}

partial class Program
{
    [GeneratedRegex(@"\{[*]?(?<name>[^}:=\?]+)(?<constraint>:[^}=\?]+)?(?<optional>\?)?(?<default>=[^}]+)?\}")]
    private static partial Regex RouteParameterRegex();
}
