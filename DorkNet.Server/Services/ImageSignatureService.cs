using System.Security.Cryptography;

namespace DorkNet.Server.Services;

public sealed class ImageSignatureService
{
    private readonly RSA? rsa;
    private readonly string keyId;
    private readonly string? configuredKeyIdHostSuffix;
    private readonly ILogger<ImageSignatureService> logger;

    public ImageSignatureService(IConfiguration config, ILogger<ImageSignatureService> logger)
    {
        this.logger = logger;
        keyId = NormalizeKeyId(
            config["ImageSigning:KeyId"]
            ?? Environment.GetEnvironmentVariable("DORKNET_IMAGE_SIGNING_KEY_ID")
            ?? "p1");

        // 2023 compares Content-Signature's key-id against the embedded
        // KEY:RSA:p1.rec.net / d1.rec.net literals. If a specific client build
        // is bytepatched to another suffix, ImageSigning:KeyIdHostSuffix can
        // override this default.
        configuredKeyIdHostSuffix =
            config["ImageSigning:KeyIdHostSuffix"]
            ?? Environment.GetEnvironmentVariable("DORKNET_IMAGE_SIGNING_KEY_ID_SUFFIX");

        var privateKey =
            config["ImageSigning:PrivateKeyPem"]
            ?? Environment.GetEnvironmentVariable("DORKNET_IMAGE_SIGNING_PRIVATE_KEY");

        var privateKeyBase64 =
            config["ImageSigning:PrivateKeyPemBase64"]
            ?? Environment.GetEnvironmentVariable("DORKNET_IMAGE_SIGNING_PRIVATE_KEY_BASE64");

        if (string.IsNullOrWhiteSpace(privateKey) && !string.IsNullOrWhiteSpace(privateKeyBase64))
        {
            try
            {
                privateKey = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(privateKeyBase64));
            }
            catch (FormatException ex)
            {
                logger.LogError(ex, "[img-sign] ImageSigning private key base64 is invalid");
            }
        }

        if (string.IsNullOrWhiteSpace(privateKey))
        {
            logger.LogWarning("[img-sign] no image signing private key configured; image downloads will use placeholder Content-Signature headers");
            return;
        }

        var normalized = NormalizePem(privateKey);
        var hasBegin = normalized.Contains("-----BEGIN", StringComparison.Ordinal);
        var lineCount = normalized.Count(c => c == '\n') + 1;
        logger.LogInformation(
            "[img-sign] parsing key: rawLen={RawLen} normLen={NormLen} hasBegin={HasBegin} lines={Lines} firstChars={First} lastChars={Last}",
            privateKey.Length, normalized.Length, hasBegin, lineCount,
            normalized.Length > 30 ? normalized[..30].Replace('\n', '·') : normalized.Replace('\n', '·'),
            normalized.Length > 30 ? normalized[^30..].Replace('\n', '·') : "");

        rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(normalized);
            logger.LogInformation("[img-sign] image response signing enabled with key {KeyId} (PEM path)", keyId);
            return;
        }
        catch (Exception pemEx)
        {
            logger.LogWarning(pemEx, "[img-sign] ImportFromPem failed, falling back to raw-DER import");
        }

        // PEM parse failed — try treating the input as raw DER bytes,
        // base64-encoded with or without PEM headers. ImportFromPem is
        // strict about line lengths and header forms; ImportPkcs8 /
        // ImportRSA* only need the DER octets and tolerate any
        // base64-encoded body.
        try
        {
            var body = ExtractBase64Body(normalized);
            var der = Convert.FromBase64String(body);
            try { rsa.ImportPkcs8PrivateKey(der, out _); }
            catch
            {
                try { rsa.ImportRSAPrivateKey(der, out _); }
                catch
                {
                    rsa.ImportEncryptedPkcs8PrivateKey(ReadOnlySpan<char>.Empty, der, out _);
                }
            }
            logger.LogInformation("[img-sign] image response signing enabled with key {KeyId} (raw-DER fallback)", keyId);
        }
        catch (Exception derEx)
        {
            rsa.Dispose();
            rsa = null;
            logger.LogError(derEx, "[img-sign] failed to load image signing private key — neither PEM nor DER parse succeeded");
        }
    }

    /// <summary>Strip PEM headers + all whitespace from
    /// <paramref name="input"/>, leaving just the base64 body. Used by
    /// the raw-DER fallback when ImportFromPem rejects the formatted
    /// PEM.</summary>
    private static string ExtractBase64Body(string input)
    {
        var s = input;
        var beginIdx = s.IndexOf("-----BEGIN", StringComparison.Ordinal);
        if (beginIdx >= 0)
        {
            var afterBegin = s.IndexOf("-----", beginIdx + 5, StringComparison.Ordinal);
            if (afterBegin >= 0) s = s[(afterBegin + 5)..];
        }
        var endIdx = s.IndexOf("-----END", StringComparison.Ordinal);
        if (endIdx >= 0) s = s[..endIdx];
        return StripWhitespace(s);
    }

    /// <summary>Coerce the configured value into something
    /// <c>RSA.ImportFromPem</c> will accept.
    ///
    /// Coolify/docker-compose let operators paste PEMs into a single-line
    /// env-var field. Depending on the panel, the multi-line key may
    /// arrive with:
    /// <list type="bullet">
    ///   <item>Surrounding quotes (<c>"-----BEGIN PRIVATE KEY-----..."</c>)</item>
    ///   <item>Literal <c>\n</c> in place of real line breaks</item>
    ///   <item>Windows CRLF line endings</item>
    ///   <item>All line breaks flattened to spaces — header and body on one line</item>
    ///   <item>The bare base64 body with the BEGIN/END markers stripped</item>
    /// </list>
    /// <c>ImportFromPem</c> rejects every variation except the canonical
    /// PEM (header line, 64-char-wrapped body lines, footer line) and
    /// throws <c>"No supported key formats were found. Check that the
    /// input represents the contents of a PEM-encoded key file, not the
    /// path to such a file."</c> — which masks every garbled-paste case
    /// behind a path-vs-content red herring. We rebuild the canonical
    /// form here.
    ///
    /// Also covers the case where someone configured the path to a
    /// file (e.g. <c>/run/secrets/img-sign-key.pem</c>); we read its
    /// contents and feed those through the same normaliser.</summary>
    private static string NormalizePem(string input)
    {
        input = (input ?? string.Empty).Trim();

        // Strip a single layer of surrounding quotes ("..." or '...').
        if (input.Length >= 2 &&
            ((input[0] == '"' && input[^1] == '"') ||
             (input[0] == '\'' && input[^1] == '\'')))
        {
            input = input[1..^1];
        }

        // Path-to-file case: read it through.
        if (!input.Contains("-----BEGIN", StringComparison.Ordinal)
            && !input.Contains("BEGIN PRIVATE", StringComparison.Ordinal)
            && input.Length < 4096
            && System.IO.File.Exists(input))
        {
            input = System.IO.File.ReadAllText(input);
        }

        // Unescape literal \n / \r\n / \r that survived env-var passthrough.
        input = input
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        // Strip shell / YAML line-continuation backslashes. Some operator
        // tools (heredocs, YAML block-scalars, multi-line env-var UIs)
        // emit each PEM line with a trailing <c>\</c> as a continuation
        // marker. If that marker isn't expanded by the surrounding shell,
        // the <c>\</c> ends up embedded right before every <c>\n</c> in
        // the PEM body. ImportFromPem tolerates it (sees corrupted body),
        // but the DER fallback's <c>FromBase64String</c> rejects <c>\</c>
        // as an illegal base64 character. Diagnosed live: env var had
        // <c>...-----END RSA PRIVATE KEY-----\\</c>+newline plus one <c>\</c>
        // per body line; ~27 stray backslashes total. Strip them.
        input = input.Replace("\\\n", "\n", StringComparison.Ordinal);
        // Also strip a single trailing backslash before EOF (no newline
        // after it — happens when the env var is set without a final \n).
        if (input.EndsWith('\\')) input = input[..^1];

        // If the headers are present but everything's on one line
        // (e.g. "-----BEGIN PRIVATE KEY----- MIIEvgIBADAN... -----END PRIVATE KEY-----"),
        // split it back into the canonical 64-char-wrapped form.
        if (input.Contains("-----BEGIN", StringComparison.Ordinal) && !input.Contains('\n'))
        {
            input = ReflowSingleLinePem(input);
        }

        // Still no headers? Treat the whole thing as the raw base64 body
        // and synthesize a PKCS#8 PEM wrapper. ImportFromPem will figure
        // out whether it's actually PKCS#1 or PKCS#8 from the DER inside.
        if (!input.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            var body = WrapBase64(StripWhitespace(input));
            input = $"-----BEGIN PRIVATE KEY-----\n{body}\n-----END PRIVATE KEY-----";
        }

        return input;
    }

    /// <summary>Pull the BEGIN/END headers off a flattened single-line
    /// PEM and re-emit it with the conventional 64-char body lines.</summary>
    private static string ReflowSingleLinePem(string flat)
    {
        var beginEnd = flat.IndexOf("-----", flat.IndexOf("-----BEGIN") + 5);
        if (beginEnd < 0) return flat;
        var headerEnd = flat.IndexOf("-----", beginEnd + 5);
        if (headerEnd < 0) return flat;
        // BEGIN line is from start to first "-----" closer + 5.
        var begin = flat[..(beginEnd + 5)].Trim();
        // END line starts at "-----END ...-----" — find the trailing block.
        var endStart = flat.LastIndexOf("-----END", StringComparison.Ordinal);
        if (endStart < 0) return flat;
        var end = flat[endStart..].Trim();
        var body = StripWhitespace(flat[(beginEnd + 5)..endStart]);
        return $"{begin}\n{WrapBase64(body)}\n{end}";
    }

    private static string StripWhitespace(string s)
    {
        // Drops whitespace AND backslashes — the latter survive when
        // line-continuation markers are still embedded in the base64
        // body and would otherwise trip FromBase64String's strict
        // "illegal character" check. Every legitimate base64 char is
        // in [A-Za-z0-9+/=], so a `\` is always wrong here.
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (!char.IsWhiteSpace(c) && c != '\\') sb.Append(c);
        return sb.ToString();
    }

    private static string WrapBase64(string body)
    {
        var sb = new System.Text.StringBuilder(body.Length + (body.Length / 64) + 1);
        for (var i = 0; i < body.Length; i += 64)
        {
            sb.Append(body, i, Math.Min(64, body.Length - i));
            if (i + 64 < body.Length) sb.Append('\n');
        }
        return sb.ToString();
    }

    public bool IsEnabled => rsa is not null;

    public void AddContentSignature(HttpResponse response, byte[] bytes)
    {
        var signature = rsa is not null && bytes.Length > 0
            ? rsa.SignData(bytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1)
            : PlaceholderSignature(bytes);

        // The client appends ?sig=<key> to image CDN requests and then
        // requires Content-Signature's key-id to match that exact signing
        // key. Prefer the request value over config so localhost/prod or
        // stale env settings cannot make otherwise-valid image bytes fail
        // as "Signature malformed".
        var requestKeyId = response.HttpContext.Request.Query["sig"].ToString();
        var headerKeyId = string.IsNullOrWhiteSpace(requestKeyId)
            ? keyId
            : NormalizeKeyId(requestKeyId);

        var suffix = configuredKeyIdHostSuffix is { Length: > 0 }
            ? configuredKeyIdHostSuffix
            : "rec.net";

        response.Headers["Content-Signature"] =
            $"key-id=KEY:RSA:{headerKeyId}.{suffix}; data={Convert.ToBase64String(signature)}";
    }

    private static byte[] PlaceholderSignature(byte[] bytes)
    {
        var signature = new byte[128];
        var seed = SHA1.HashData(bytes);
        for (var i = 0; i < signature.Length; i++)
            signature[i] = seed[i % seed.Length];
        return signature;
    }

    private static string NormalizeKeyId(string keyId)
    {
        keyId = keyId.Trim();
        if (keyId.StartsWith("key-id=", StringComparison.OrdinalIgnoreCase))
            keyId = keyId["key-id=".Length..].Trim();
        if (keyId.StartsWith("KEY:RSA:", StringComparison.OrdinalIgnoreCase))
            keyId = keyId["KEY:RSA:".Length..].Trim();
        // Strip a trailing host suffix if one slipped into the key-id (e.g.
        // someone passed `?sig=p1.localhost`). We only care about the bare
        // "p1"/"d1" tag; the host part is appended fresh by AddContentSignature.
        var dot = keyId.IndexOf('.');
        if (dot > 0) keyId = keyId[..dot];
        return string.IsNullOrWhiteSpace(keyId) ? "p1" : keyId;
    }
}
