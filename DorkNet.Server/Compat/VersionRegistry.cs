namespace DorkNet.Server.Compat;

/// <summary>Resolves a client-build key (e.g. <c>december_2020_12_18</c>)
/// to its <see cref="ClientVersion"/>. Built once at startup from all
/// registered <see cref="IVersionPlugin"/> instances.
///
/// <para>Singleton — the version-key → generation mapping is fixed at
/// boot time, and the registry is read on every request by the
/// version-detection middleware.</para>
/// </summary>
public sealed class VersionRegistry
{
    private readonly Dictionary<string, ClientVersion> _byKey;

    /// <summary>The set of version keys this deployment is willing to
    /// serve. Comes from <c>DorkNet:SupportedVersions</c> in config.
    /// Anything else gets a 426 from the middleware.</summary>
    public IReadOnlySet<string> SupportedVersionKeys { get; }

    /// <summary>Falls back to this when a request arrives without an
    /// <c>X-DorkNet-Version</c> header — set via
    /// <c>DorkNet:DefaultClientVersion</c> in config. Empty/null means
    /// "no fallback; reject headerless requests". Set during the
    /// transition window where the old client mod doesn't yet stamp
    /// the header.</summary>
    public string? DefaultVersionKey { get; }

    public VersionRegistry(
        IEnumerable<IVersionPlugin> plugins,
        IReadOnlySet<string> supportedVersionKeys,
        string? defaultVersionKey)
    {
        _byKey = new Dictionary<string, ClientVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            foreach (var key in plugin.VersionKeys)
            {
                if (_byKey.ContainsKey(key))
                    throw new InvalidOperationException(
                        $"VersionPlugin collision: '{key}' is claimed by more than one IVersionPlugin. " +
                        $"Each version key maps to exactly one plugin.");
                _byKey[key] = new ClientVersion(key, plugin.Generation);
            }
        }
        SupportedVersionKeys = supportedVersionKeys;
        DefaultVersionKey = defaultVersionKey;
    }

    /// <summary>Resolve a version key (case-insensitive) to its
    /// <see cref="ClientVersion"/>, or <see cref="ClientVersion.Unknown"/>
    /// if no plugin claims it. The middleware decides whether unknown
    /// → reject or → fall back to <see cref="DefaultVersionKey"/>.</summary>
    public ClientVersion Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return ClientVersion.Unknown;
        return _byKey.TryGetValue(key, out var v) ? v : ClientVersion.Unknown;
    }

    /// <summary>True when the given version key is in
    /// <see cref="SupportedVersionKeys"/>. Used by the middleware to
    /// 426 cross-version connection attempts (e.g. a March client
    /// hitting a server configured for December only).</summary>
    public bool IsSupported(string key)
        => SupportedVersionKeys.Contains(key);

    /// <summary>Every known plugin-registered version key, for
    /// diagnostic responses and admin tooling.</summary>
    public IEnumerable<ClientVersion> AllKnown => _byKey.Values;
}
