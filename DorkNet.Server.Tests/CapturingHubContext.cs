using System.Collections.Concurrent;
using System.Text.Json;
using DorkNet.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DorkNet.Server.Tests;

/// <summary>
/// Stand-in for the notify hub that records every push instead of sending it,
/// so tests can assert on the exact frames the game client would receive.
/// A frame is the hub method plus its single string argument, parsed back into
/// the <c>{Id, Msg}</c> envelope.
/// </summary>
public sealed class CapturingHubContext : IHubContext<NotifyHub>
{
    private readonly ConcurrentQueue<SentFrame> _sent = new();

    public IHubClients Clients => new CapturingClients(this);
    public IGroupManager Groups { get; } = new NoopGroups();

    public IReadOnlyList<SentFrame> Sent => _sent.ToArray();

    /// <summary>Frames sent to a target such as <c>group:player:12</c>, in
    /// order, optionally filtered by envelope <c>Id</c>.</summary>
    public IReadOnlyList<SentFrame> To(string target, string? id = null) =>
        _sent.Where(f => f.Target == target && (id is null || f.Id == id)).ToList();

    public static string PlayerGroup(long playerId) => $"group:{NotifyHub.GroupForPlayer(playerId)}";

    internal void Record(string target, IReadOnlyList<string>? excluded, string method, object?[] args) =>
        _sent.Enqueue(new SentFrame(target, excluded ?? Array.Empty<string>(), method,
            args.Length == 1 && args[0] is string s ? s : JsonSerializer.Serialize(args)));

    public sealed record SentFrame(string Target, IReadOnlyList<string> Excluded, string Method, string Payload)
    {
        public JsonElement Envelope => JsonDocument.Parse(Payload).RootElement;
        public string? Id => Envelope.TryGetProperty("Id", out var id) ? id.GetString() : null;
        public JsonElement Msg => Envelope.GetProperty("Msg");
    }

    private sealed class CapturingClients(CapturingHubContext owner) : IHubClients
    {
        private IClientProxy Proxy(string target, IReadOnlyList<string>? excluded = null) =>
            new CapturingProxy(owner, target, excluded);

        public IClientProxy All => Proxy("all");
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy("all", excludedConnectionIds);
        public IClientProxy Client(string connectionId) => Proxy($"conn:{connectionId}");
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy($"conns:{string.Join(",", connectionIds)}");
        public IClientProxy Group(string groupName) => Proxy($"group:{groupName}");
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            Proxy($"group:{groupName}", excludedConnectionIds);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FanOutProxy(groupNames.Select(g => Proxy($"group:{g}")).ToList());
        public IClientProxy User(string userId) => Proxy($"user:{userId}");
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy($"users:{string.Join(",", userIds)}");
    }

    private sealed class CapturingProxy(CapturingHubContext owner, string target, IReadOnlyList<string>? excluded) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            owner.Record(target, excluded, method, args);
            return Task.CompletedTask;
        }
    }

    private sealed class FanOutProxy(IReadOnlyList<IClientProxy> proxies) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            Task.WhenAll(proxies.Select(p => p.SendCoreAsync(method, args, cancellationToken)));
    }

    private sealed class NoopGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
