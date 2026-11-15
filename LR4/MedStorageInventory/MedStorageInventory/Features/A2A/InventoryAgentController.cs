using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MedStorage.Contracts;
using MedStorage.Inventory.Api.Domain.Entities;
using MedStorage.Inventory.Api.Features.Kits.GetKitItems;
using MedStorage.Inventory.Api.Infrastructure.Database;

namespace MedStorage.Inventory.Api.Features.A2A;

// A2A server side of the Producer agent (card + JSON-RPC message/send).
// The publish_medicine_added skill shows how the Producer forms a task for the Consumer agent.
[ApiController]
public class InventoryAgentController(
    InventoryDbContext dbContext,
    IMediator mediator,
    A2AAgentClient alertsAgent,
    IConfiguration configuration,
    ILogger<InventoryAgentController> logger) : ControllerBase
{
    private const string RequiredPeerSkill = "track_medicine_expiry";

    [HttpGet("/.well-known/agent-card.json")]
    public AgentCard GetAgentCard() =>
        InventoryAgentCard.Build(configuration["A2A:PublicUrl"] ?? $"{Request.Scheme}://{Request.Host}");

    [HttpPost("/a2a")]
    public async Task<JsonRpcResponse> HandleJsonRpc([FromBody] JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (request.Jsonrpc != "2.0" || request.Method != "message/send")
            return new JsonRpcResponse(request.Id, Error: new JsonRpcError(JsonRpcResponse.Codes.MethodNotFound,
                "Only JSON-RPC 2.0 'message/send' is supported."));

        MessageSendParams? sendParams;
        try
        {
            sendParams = request.Params.Deserialize<MessageSendParams>(JsonSerializerOptions.Web);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            sendParams = null;
        }
        if (sendParams?.Message is not { } message)
            return new JsonRpcResponse(request.Id, Error: new JsonRpcError(JsonRpcResponse.Codes.InvalidParams, "params.message is required."));

        var skillId = message.Metadata?.TryGetValue("skillId", out var skill) == true && skill.ValueKind == JsonValueKind.String
            ? skill.GetString()
            : null;
        var data = message.Parts.FirstOrDefault(p => p.Kind == "data")?.Data;
        var contextId = message.ContextId ?? Guid.NewGuid().ToString();

        var (state, text, artifacts) = skillId switch
        {
            InventoryAgentCard.PublishMedicineAdded => await PublishMedicineAddedAsync(data, contextId, cancellationToken),
            InventoryAgentCard.GetKitInventory => await GetKitInventoryAsync(data, cancellationToken),
            _ => (TaskState.Rejected, $"Unknown skill '{skillId}'. See /.well-known/agent-card.json.", new List<A2AArtifact>())
        };

        var taskId = Guid.NewGuid().ToString();
        var reply = new A2AMessage("agent", [Part.FromText(text)], Guid.NewGuid().ToString(), taskId, contextId);
        return new JsonRpcResponse(request.Id, new A2ATask(
            taskId, contextId, new A2ATaskStatus(state, reply, DateTime.UtcNow), artifacts,
            [message with { TaskId = taskId, ContextId = contextId }, reply]));
    }

    // Producer -> Consumer delegation (LR4, Task 3.3)
    private async Task<(string, string, List<A2AArtifact>)> PublishMedicineAddedAsync(
        JsonElement? data, string contextId, CancellationToken cancellationToken)
    {
        if (data is not { } d || !d.TryGetProperty("itemId", out var idProp) || !idProp.TryGetGuid(out var itemId))
            return (TaskState.Rejected, "Data part {\"itemId\": \"<guid>\"} is required.", []);

        var item = await dbContext.MedicineItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
            return (TaskState.Rejected, $"Item {itemId} not found.", []);

        // 1. Discovery: read the peer's Agent Card and check it really offers the skill we need
        AgentCard peer;
        try
        {
            peer = await alertsAgent.DiscoverAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or A2AException or JsonException)
        {
            return (TaskState.Failed, $"Consumer agent is unreachable: {ex.Message}", []);
        }
        if (peer.Skills.All(s => s.Id != RequiredPeerSkill))
            return (TaskState.Failed, $"Agent '{peer.Name}' has no '{RequiredPeerSkill}' skill.", []);

        // 2. Form the task: the same MedicineItemAddedEvent contract as on RabbitMQ, carried as an A2A data part.
        // metadata.skillId selects the skill from the peer's card; contextId links the two agents' tasks.
        var message = new A2AMessage(
            Role: "user",
            Parts:
            [
                Part.FromText($"New medicine batch {item.Id} was added to kit {item.KitId}. Track its expiry."),
                Part.FromData(new MedicineItemAddedEvent(
                    item.Id, item.KitId, item.DrugRefId, item.Quantity, item.FactoryExpiryDate, item.AddedAt))
            ],
            MessageId: Guid.NewGuid().ToString(),
            ContextId: contextId,
            Metadata: new Dictionary<string, JsonElement>
            {
                ["skillId"] = JsonSerializer.SerializeToElement(RequiredPeerSkill),
                ["senderAgent"] = JsonSerializer.SerializeToElement("MedStorage Inventory Agent")
            });

        // 3. Send it and react to the peer's verdict (synchronous request/response, unlike fire-and-forget RabbitMQ)
        A2ATask remote;
        try
        {
            remote = await alertsAgent.SendTaskAsync(peer, message, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or A2AException or JsonException)
        {
            return (TaskState.Failed, $"Delegation to '{peer.Name}' failed: {ex.Message}", []);
        }

        logger.LogInformation("A2A task {RemoteTaskId} on {Agent} finished with state {State}",
            remote.Id, peer.Name, remote.Status.State);

        // 4. Saga state from the consumer's answer: completed -> Active, otherwise -> Failed
        item.Status = remote.Status.State == TaskState.Completed ? MedicineItemStatus.Active : MedicineItemStatus.Failed;
        await dbContext.SaveChangesAsync(cancellationToken);

        var peerText = remote.Status.Message?.Parts.FirstOrDefault(p => p.Kind == "text")?.Text;
        var artifact = new A2AArtifact(Guid.NewGuid().ToString(), "delegation-result",
        [
            Part.FromData(new
            {
                itemId = item.Id,
                itemStatus = item.Status.ToString(),
                delegatedTo = peer.Name,
                remoteTaskId = remote.Id,
                remoteState = remote.Status.State,
                remoteArtifacts = remote.Artifacts
            })
        ]);

        return (remote.Status.State == TaskState.Completed ? TaskState.Completed : TaskState.Failed,
            $"Delegated to {peer.Name}: {peerText}", [artifact]);
    }

    private async Task<(string, string, List<A2AArtifact>)> GetKitInventoryAsync(JsonElement? data, CancellationToken cancellationToken)
    {
        if (data is not { } d || !d.TryGetProperty("kitId", out var idProp) || !idProp.TryGetGuid(out var kitId))
            return (TaskState.Rejected, "Data part {\"kitId\": \"<guid>\"} is required.", []);

        var items = await mediator.Send(new GetKitItemsQuery(kitId, null), cancellationToken);
        return (TaskState.Completed, $"Kit {kitId} has {items.Count} batch(es).",
            [new A2AArtifact(Guid.NewGuid().ToString(), "kit-items", [Part.FromData(items)])]);
    }
}
