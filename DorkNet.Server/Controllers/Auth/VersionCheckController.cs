using DorkNet.Server.Compat;
using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.Auth;

/// <summary>The boot-time client-version gate the 2023.03.21 client hits
/// at startup as <c>GET api/versioncheck/v4?v={build}&amp;p={platform}</c>.
///
/// <para>Verified against the watch's <c>KMDHPCHFADM.VerifyGameVersion</c>
/// (decompiled): the response is parsed into <c>JFKEIPDCCHH</c> which
/// exposes a single <c>VersionStatus</c> property of enum type
/// <c>PCBCGDHBAEL</c>:
/// <list type="bullet">
///   <item>0 = <c>ValidForPlay</c> — proceed normally</item>
///   <item>1 = <c>ValidForMenu</c> — let user reach menus but block matchmaking</item>
///   <item>2 = <c>UpdateRequired</c> — watch shows its built-in
///   "Update Required" dialog and refuses to advance past the boot screen</item>
/// </list>
/// This is the CLEAN way to reject a wrong-build client at the
/// front door — the watch has dedicated UX for it, no custom client
/// mod work needed.</para>
///
/// <para>The <c>v</c> query param is the raw build identifier supplied
/// by the client. We map raw build IDs to our internal
/// <see cref="ClientVersion.Key"/>s via the
/// <c>DorkNet:BuildIdToVersionKey</c> config map; any build ID we
/// don't recognise OR that maps to a key not in
/// <see cref="VersionRegistry.SupportedVersionKeys"/> gets
/// <c>VersionStatus = 2</c> and the watch refuses to proceed.</para>
///
/// <para>This endpoint is EXEMPT from
/// <c>VersionDetectionMiddleware</c>'s gate (otherwise the gate would
/// 426 the rejection request itself before this endpoint could reply
/// with the polite UpdateRequired payload). The exemption is set in
/// <c>VersionDetectionMiddleware.IsExempt</c>.</para>
/// </summary>
[ApiController]
public class VersionCheckController(
    IConfiguration config,
    VersionRegistry registry,
    ILogger<VersionCheckController> logger) : ControllerBase
{
    // Mirrors the PCBCGDHBAEL enum the watch decodes the response into.
    private const int VersionStatus_ValidForPlay = 0;
    private const int VersionStatus_ValidForMenu = 1;
    private const int VersionStatus_UpdateRequired = 2;

    private static readonly IReadOnlyDictionary<string, string> BuiltInBuildIdToVersionKey =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["20230317"] = "march_2023_03_21",
            ["20230321"] = "march_2023_03_21",
        };

    [HttpGet("/api/versioncheck/v4")]
    [HttpGet("/api/versioncheck/v3")]
    [HttpGet("/api/versioncheck/v2")]
    [HttpGet("/api/versioncheck/v1")]
    [HttpGet("/api/versioncheck")]
    [HttpGet("/api/version/v1")]
    [HttpGet("/api/version")]
    public IActionResult Check(
        [FromQuery(Name = "v")] string? buildId,
        [FromQuery(Name = "p")] string? platform)
    {
        // Map raw build ID → ClientVersion key via config. Format:
        //     "DorkNet": {
        //       "BuildIdToVersionKey": {
        //         "20230321": "march_2023_03_21"
        //       }
        //     }
        // Mapping ABSENT → unknown build → UpdateRequired.
        var mapSection = config.GetSection("DorkNet:BuildIdToVersionKey");
        string? versionKey = null;
        if (!string.IsNullOrWhiteSpace(buildId))
        {
            var normalizedBuildId = buildId.Trim();
            versionKey = mapSection[normalizedBuildId];
            if (string.IsNullOrWhiteSpace(versionKey)
                && BuiltInBuildIdToVersionKey.TryGetValue(normalizedBuildId, out var builtInVersionKey))
            {
                versionKey = builtInVersionKey;
            }
        }

        bool allowed = versionKey is not null && registry.IsSupported(versionKey);
        var status = allowed ? VersionStatus_ValidForPlay : VersionStatus_UpdateRequired;

        logger.LogInformation(
            "[versioncheck] buildId={Build} platform={Platform} → versionKey={Key} status={Status} ({Label})",
            buildId, platform, versionKey ?? "(unmapped)", status,
            status == VersionStatus_ValidForPlay ? "ValidForPlay" : "UpdateRequired");

        // Wire shape verified against KMDHPCHFADM_NestedType_JFKEIPDCCHH.txt:
        // single key "VersionStatus" (PascalCase, case-sensitive — LitJson).
        return Ok(new { VersionStatus = status });
    }
}
