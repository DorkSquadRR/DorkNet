using DorkNet.Server.Compat;

namespace DorkNet.Server.Versions.Late2020;

/// <summary>Version marker for the 2023.03.21 client branch. This
/// branch is intentionally single-client: it does not advertise or
/// serve older 2020 build keys.
/// </summary>
public sealed class Late2020VersionPlugin : IVersionPlugin
{
    public const string GenerationKey = "March2023";

    public IReadOnlyCollection<string> VersionKeys { get; } = new[]
    {
        "march_2023_03_21",
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
