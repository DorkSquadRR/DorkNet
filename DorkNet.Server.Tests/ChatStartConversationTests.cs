using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DorkNet.Server.Tests;

/// <summary>
/// Starting a chat with a player is a two-step handshake, and both steps have to
/// work on a thread that has never carried a message:
///
///   POST thread/withmembers?ids={playerId}   -> creates/returns the thread
///   GET  thread/{id}                         -> opens it
///
/// Observed failing in production: the POST returned 200 at 06:38:20.862 and the
/// GET 404'd 415ms later, because the handler decided a thread existed by
/// counting its messages. A brand-new conversation has none, so the client was
/// handed a thread id it was then told did not exist, and no chat could ever be
/// opened with someone you had not already messaged.
/// </summary>
public sealed class ChatStartConversationTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public ChatStartConversationTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Brand_new_conversation_can_be_opened_before_any_message_is_sent()
    {
        using var starterClient = Client();
        using var otherClient = Client();

        var starter = await GameClientSessionFactory.CreateAsync(starterClient, _factory.ApexDomain);
        var other = await GameClientSessionFactory.CreateAsync(otherClient, _factory.ApexDomain);

        starterClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", starter.AccessToken);

        // Step 1 — the client asks for the thread with that player. It sends the
        // ids as a form body (observed on the wire as
        // "ids=9741717&messageCount=50"), which also matters for routing: the
        // content type is what separates this from POST thread/{chatThreadId},
        // whose parameter segment otherwise also matches "withmembers".
        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("ids", other.PlayerId.ToString()),
            new KeyValuePair<string, string>("messageCount", "50"),
        ]);
        using var createResponse = await starterClient.PostAsync("/thread/withmembers", form);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.IsSuccessStatusCode,
            $"POST /thread/withmembers -> {(int)createResponse.StatusCode}: {createBody}");

        var thread = JsonDocument.Parse(createBody).RootElement;
        Assert.True(thread.TryGetProperty("ChatThreadId", out var idElement)
                    || thread.TryGetProperty("chatThreadId", out idElement),
            $"no thread id in the withmembers reply: {createBody}");
        var threadId = idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : idElement.ToString();
        Assert.False(string.IsNullOrWhiteSpace(threadId));

        // Step 2 — and immediately opens it. No message has been sent yet.
        using var openResponse = await starterClient.GetAsync($"/thread/{threadId}?maxCount=50");
        var openBody = await openResponse.Content.ReadAsStringAsync();

        Assert.True(openResponse.StatusCode != HttpStatusCode.NotFound,
            $"""
             GET /thread/{threadId} returned 404 for a thread that thread/withmembers
             had just created. An empty thread is not a missing thread — the chat
             cannot be opened until someone has already sent a message, which is
             impossible to do first.
             """);
        Console.WriteLine("=== WITHMEMBERS ===");
        Console.WriteLine(createBody);
        Console.WriteLine("=== GET THREAD ===");
        Console.WriteLine(openBody);

        Assert.True(openResponse.IsSuccessStatusCode,
            $"GET /thread/{threadId} -> {(int)openResponse.StatusCode}: {openBody}");
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://chat.{_factory.ApexDomain}");
        return client;
    }
}
