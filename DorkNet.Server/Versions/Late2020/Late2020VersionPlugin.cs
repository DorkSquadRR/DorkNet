using DorkNet.Server.Compat;

namespace DorkNet.Server.Versions.Late2020;

/// <summary>The "Late 2020" wire-format generation: covers all Rec Room
/// builds from the autumn 2020 schema migration through the end of the
/// year. The 2020.12.18 build is the canonical member; if/when we add
/// 2020.10 or 2020.11 builds and find their wire shapes are identical,
/// we just list them in <see cref="VersionKeys"/> here — no new code.
///
/// <para>Currently this plugin is a marker: it declares which version
/// keys belong to the generation, but no strategy services are
/// registered yet. As controllers start branching on
/// <see cref="HttpContextExtensions.ClientVersion"/>'s
/// <c>Generation</c>, the strategies that get extracted will register
/// themselves through <see cref="RegisterStrategies"/> with a
/// keyed-DI binding so the right implementation resolves per
/// request.</para>
/// </summary>
public sealed class Late2020VersionPlugin : IVersionPlugin
{
    public const string GenerationKey = "Late2020";

    public IReadOnlyCollection<string> VersionKeys { get; } = new[]
    {
        // Verified working against this server. The canonical
        // private-deployment build the codebase has been developed
        // against since the move to localhost.
        "december_2020_12_18",
    };

    public string Generation => GenerationKey;

    public void RegisterStrategies(IServiceCollection services)
    {
        // Intentionally empty for now. Generation-specific strategy
        // services land here as soon as the first controller diverges
        // (e.g. an IWireFormat keyed on GenerationKey, an IRouteOverride,
        // etc.). Today every wire shape is implicitly "Late2020" so
        // there's nothing to differentiate.
    }
}
