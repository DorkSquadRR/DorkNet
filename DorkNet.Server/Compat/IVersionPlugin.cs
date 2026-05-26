namespace DorkNet.Server.Compat;

/// <summary>One client-version-family plugin. Each implementation
/// declares which exact version <see cref="VersionKeys"/> it handles
/// and which protocol-compatibility <see cref="Generation"/> they map
/// to. The plugin's job at registration time is to also wire any
/// generation-specific strategy services into DI via
/// <see cref="RegisterStrategies"/> so controllers / services
/// downstream can resolve the right <c>IWireFormat</c>,
/// <c>IRouteOverride</c>, etc. for the active request's generation.
///
/// <para>Adding a new version with the SAME wire shapes as an existing
/// generation = just listing its key in that generation's plugin
/// <see cref="VersionKeys"/>. Only a new generation needs new
/// strategy classes.</para>
/// </summary>
public interface IVersionPlugin
{
    /// <summary>All client-build keys this plugin handles. Multiple
    /// keys can share one plugin when several Rec Room builds shipped
    /// the same wire format. Convention: lowercase, underscore-separated,
    /// matching the literal the client mod sends in
    /// <c>X-DorkNet-Version</c>.</summary>
    IReadOnlyCollection<string> VersionKeys { get; }

    /// <summary>Wire-format-compatibility group this plugin implements.
    /// Multiple plugins can share a generation (rare, but allowed when
    /// e.g. a Quest build and a PC build of the same era are
    /// protocol-identical and we just want separate
    /// <see cref="VersionKeys"/> bookkeeping).</summary>
    string Generation { get; }

    /// <summary>Register any generation-specific strategy services
    /// into DI. Called once at startup by the bootstrap code in
    /// <c>Program.cs</c>. Use keyed registrations (keyed by
    /// <see cref="Generation"/>) so requests for other generations
    /// don't collide.</summary>
    void RegisterStrategies(IServiceCollection services);
}
