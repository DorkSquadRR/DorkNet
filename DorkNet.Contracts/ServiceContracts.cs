namespace DorkNet.Contracts;

public sealed record ServiceHealthResponse(
    string Service,
    string Status,
    DateTimeOffset CheckedAtUtc);

public sealed record ServiceCapabilityResponse(
    string Service,
    string[] Owns,
    string[] PlannedPublicRoutes);

public sealed record ServiceProbeResponse(
    string Service,
    string BaseUrl,
    string Status,
    int? StatusCode,
    string? Error,
    DateTimeOffset CheckedAtUtc);
