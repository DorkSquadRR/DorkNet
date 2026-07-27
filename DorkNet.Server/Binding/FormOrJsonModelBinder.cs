using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DorkNet.Server.Binding;

/// <summary>
/// Binds an action's request DTO from either a form-urlencoded body or a JSON
/// body, whichever the caller actually sent.
///
/// The Rec Room client's HTTP layer (<c>BNDIAONDFFF</c> in RecNet.Runtime)
/// sends nearly every write as <c>application/x-www-form-urlencoded</c>, adding
/// fields one at a time. Handlers written against <c>[FromBody]</c> only accept
/// JSON, so with <c>[ApiController]</c> those requests were rejected with 415
/// Unsupported Media Type before the action body ever ran — this is why every
/// club member-management action (invite, ban, kick, promote, approve, modify)
/// failed on the 2023 client.
///
/// Rather than hand-rolling a form reader per handler, apply this binder to the
/// DTO parameter:
///
/// <code>
/// public Task&lt;IActionResult&gt; MemberBan(
///     long clubId,
///     [ModelBinder(typeof(FormOrJsonModelBinder))] MemberTargetRequest req)
/// </code>
///
/// The explicit <c>[ModelBinder]</c> is required: without it <c>[ApiController]</c>
/// infers <c>[FromBody]</c> for complex types and this binder never runs.
///
/// Matching is case-insensitive on both paths, and a scalar property also
/// accepts the singular/plural spellings the client uses interchangeably. A
/// missing or unparseable body yields a default-constructed DTO rather than a
/// 400, matching how the client treats these calls as fire-and-forget.
/// </summary>
public sealed class FormOrJsonModelBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var type = bindingContext.ModelType;
        var request = bindingContext.HttpContext.Request;
        var model = Activator.CreateInstance(type)!;

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(bindingContext.HttpContext.RequestAborted);
            foreach (var prop in Writable(type))
            {
                var values = Lookup(form, prop.Name);
                if (values.Count == 0) continue;
                var converted = Convert(values, prop.PropertyType);
                if (converted is not null) prop.SetValue(model, converted);
            }
        }
        else
        {
            try
            {
                request.EnableBuffering();
                request.Body.Position = 0;
                var parsed = await JsonSerializer.DeserializeAsync(
                    request.Body, type,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    bindingContext.HttpContext.RequestAborted);
                request.Body.Position = 0;
                if (parsed is not null) model = parsed;
            }
            catch (JsonException) { /* empty or non-JSON body → defaults */ }
            catch (NotSupportedException) { /* unreadable body → defaults */ }
        }

        bindingContext.Result = ModelBindingResult.Success(model);
    }

    private static IEnumerable<PropertyInfo> Writable(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite);

    /// <summary>Form keys, case-insensitively, also accepting the other
    /// number of the name — the client says <c>accountId</c> where the DTO says
    /// <c>AccountIds</c> and vice versa depending on the endpoint.</summary>
    private static List<string> Lookup(IFormCollection form, string propertyName)
    {
        var candidates = new List<string> { propertyName };
        if (propertyName.EndsWith('s')) candidates.Add(propertyName[..^1]);
        else candidates.Add(propertyName + "s");

        foreach (var candidate in candidates)
        {
            foreach (var key in form.Keys)
            {
                if (!string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase)) continue;
                var values = form[key]
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!)
                    .ToList();
                if (values.Count > 0) return values;
            }
        }
        return [];
    }

    private static object? Convert(List<string> values, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))
        {
            var element = underlying.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(underlying)!;
            // A repeated field arrives as multiple values; a single field may
            // still carry a comma-separated list.
            foreach (var raw in values.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                var item = ConvertScalar(raw, element);
                if (item is not null) list.Add(item);
            }
            return list.Count > 0 ? list : null;
        }

        return ConvertScalar(values[0], underlying);
    }

    private static object? ConvertScalar(string raw, Type target)
    {
        try
        {
            if (target == typeof(string)) return raw;
            if (target == typeof(bool)) return bool.TryParse(raw, out var b) ? b : raw == "1";
            if (target.IsEnum) return Enum.TryParse(target, raw, ignoreCase: true, out var e) ? e : null;
            if (target == typeof(Guid)) return Guid.TryParse(raw, out var g) ? g : null;
            if (target == typeof(DateTime)) return DateTime.TryParse(raw, out var d) ? d : null;
            return System.Convert.ChangeType(raw, target, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
