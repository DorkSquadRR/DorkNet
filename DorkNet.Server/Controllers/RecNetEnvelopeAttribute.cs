using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DorkNet.Server.Controllers;

/// <summary>
/// Wraps an action's reply in the legacy <c>{success, value, error}</c>
/// envelope the 2023 rooms client reads for every room and playlist mutation.
///
/// <para><b>Client contract.</b> All of these calls go through one generic
/// helper, <c>NLDBPDCNNCF.BDBHCEGKGDC&lt;TResult&gt;(string opName, HTTPMethods,
/// string route, …)</c> (RVA 0x35E50A0, recnet-runtime-decomp/NLDBPDCNNCF.cs:2310;
/// called as <c>Call 0x1835E50A0</c> 47 times in
/// IsilDump/RecNet.Runtime/NLDBPDCNNCF.txt). It is the same helper, with the
/// same <c>BDBHCEGKGDC&lt;FGCPNAACHIK&gt;</c> instantiation token
/// <c>[0x1884C1948]</c>, that "clone room" uses (NLDBPDCNNCF.txt:4815-4823), and
/// clone is proven in game to need <c>{success, value, error}</c>
/// (docs/recroom-2023-room-save.md). A bare room-details body has no
/// <c>success</c>, so the helper rejects it with no message and the player sees
/// <c>Exception of type 'CEMNLBKJABA' was thrown.</c> even though the server
/// applied the change. The routes are: rooms/{0}/{accessibility,
/// allow_new_users, automute, cloning, comments, creator, description, image,
/// loadscreen, max_player_calculation_mode, min_level, modify, name,
/// promo_external, promo_images, restrictions, tags, voice_chat_encryption,
/// warning}, rooms/{0}/roles/{1}[/invite], rooms/{0}/subrooms[/{1}[/accessibility,
/// clone, maxplayers, modify, move, name, permissions, publish_save]],
/// playlists and playlists/{0}/{accessibility, description, image, levelvoting,
/// name, restrictions, rooms/{1}, tags, warning}.</para>
///
/// <para>A 2xx object result becomes <c>{success:true, value, error:""}</c>.
/// Anything else becomes HTTP 200 <c>{success:false, value:null, error}</c>
/// with a message the client can show, because a non-2xx reply also surfaces
/// only as the message-less CEMNLBKJABA.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RecNetEnvelopeAttribute : ResultFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        context.Result = Wrap(context.Result);
        base.OnResultExecuting(context);
    }

    public static IActionResult Wrap(IActionResult result)
    {
        switch (result)
        {
            case ObjectResult obj when obj.StatusCode is null or (>= 200 and < 300):
                return IsEnvelope(obj.Value)
                    ? result
                    : Success(obj.Value);
            case ObjectResult obj:
                return Failure(ErrorText(obj.Value) ?? DefaultError(obj.StatusCode ?? 500));
            case ForbidResult or ChallengeResult or UnauthorizedResult:
                return Failure(DefaultError(403));
            case StatusCodeResult code when code.StatusCode is >= 200 and < 300:
                return Success(null);
            case StatusCodeResult code:
                return Failure(DefaultError(code.StatusCode));
            case EmptyResult:
                return Success(null);
            default:
                return result;
        }
    }

    public static OkObjectResult Success(object? value) =>
        new(new { success = true, value, error = string.Empty });

    public static OkObjectResult Failure(string error) =>
        new(new { success = false, value = (object?)null, error });

    private static string DefaultError(int status) => status switch
    {
        400 => "That request was not valid.",
        401 or 403 => "You don't have permission to do that.",
        404 => "That room doesn't exist.",
        409 => "That name is already taken.",
        _ => "Something went wrong.",
    };

    /// <summary>An <c>error</c>/<c>Error</c> string on the failure body, if any.</summary>
    private static string? ErrorText(object? value)
    {
        if (value is null) return null;
        if (value is string s) return string.IsNullOrWhiteSpace(s) ? null : s;
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Name.Equals("error", StringComparison.OrdinalIgnoreCase)) continue;
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var text = prop.Value.GetString();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
            }
        }
        catch (NotSupportedException)
        {
        }
        return null;
    }

    private static bool IsEnvelope(object? value)
    {
        if (value is null) return false;
        var props = value.GetType().GetProperties();
        bool Has(string name) => props.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Has("success") && Has("value") && Has("error");
    }
}
