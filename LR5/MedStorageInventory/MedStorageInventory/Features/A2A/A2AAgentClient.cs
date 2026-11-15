using System.Net.Http.Json;
using System.Text.Json;

namespace MedStorage.Inventory.Api.Features.A2A;

public class A2AException(string message) : Exception(message);

// A2A client side of the Producer agent. Decentralized: there is no broker in the middle,
// the producer only knows the base URL of a peer, reads its Agent Card and talks to it directly.
public class A2AAgentClient(HttpClient httpClient, ILogger<A2AAgentClient> logger)
{
    // Step 1 - discovery: GET <peer>/.well-known/agent-card.json
    public async Task<AgentCard> DiscoverAsync(CancellationToken cancellationToken)
    {
        var card = await httpClient.GetFromJsonAsync<AgentCard>(
            "/.well-known/agent-card.json", JsonSerializerOptions.Web, cancellationToken)
            ?? throw new A2AException("Peer returned an empty Agent Card.");

        logger.LogInformation("Discovered A2A agent {Name} v{Version} with skills [{Skills}]",
            card.Name, card.Version, string.Join(", ", card.Skills.Select(s => s.Id)));
        return card;
    }

    // Step 2 - delegation: JSON-RPC "message/send" to the endpoint advertised in the card
    public async Task<A2ATask> SendTaskAsync(AgentCard card, A2AMessage message, CancellationToken cancellationToken)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "message/send",
            @params = new { message, configuration = new { blocking = true, acceptedOutputModes = new[] { "application/json" } } }
        };

        using var response = await httpClient.PostAsJsonAsync(card.Url, request, JsonSerializerOptions.Web, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rpc = await response.Content.ReadFromJsonAsync<JsonElement>(JsonSerializerOptions.Web, cancellationToken);
        if (rpc.TryGetProperty("error", out var error))
            throw new A2AException($"Peer agent error: {error}");

        return rpc.GetProperty("result").Deserialize<A2ATask>(JsonSerializerOptions.Web)
               ?? throw new A2AException("Peer agent returned no task.");
    }
}
