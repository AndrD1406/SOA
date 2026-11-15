using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using MedStorage.Alerts.Api.Features.Alerts.GetExpiringItems;
using MedStorage.Alerts.Api.Features.Tracking;
using MedStorage.Contracts;

namespace MedStorage.Alerts.Api.Features.A2A;

// A2A server side of the Consumer agent: discovery (Agent Card) + JSON-RPC endpoint.
// Supported methods: message/send (creates and runs a task synchronously) and tasks/get.
[ApiController]
public class AlertsAgentController(
    ExpiryTracker tracker,
    IMediator mediator,
    A2ATaskStore taskStore,
    IConfiguration configuration,
    ILogger<AlertsAgentController> logger) : ControllerBase
{
    [HttpGet("/.well-known/agent-card.json")]
    public AgentCard GetAgentCard() =>
        AlertsAgentCard.Build(configuration["A2A:PublicUrl"] ?? $"{Request.Scheme}://{Request.Host}");

    [HttpPost("/a2a")]
    public async Task<JsonRpcResponse> HandleJsonRpc([FromBody] JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (request.Jsonrpc != "2.0")
            return Error(request.Id, JsonRpcResponse.Codes.InvalidRequest, "jsonrpc must be \"2.0\".");

        try
        {
            return request.Method switch
            {
                "message/send" => new JsonRpcResponse(request.Id,
                    await HandleMessageAsync(request.Params.Deserialize<MessageSendParams>(JsonSerializerOptions.Web)!, cancellationToken)),
                "tasks/get" => taskStore.Get(request.Params.Deserialize<TaskQueryParams>(JsonSerializerOptions.Web)!.Id) is { } task
                    ? new JsonRpcResponse(request.Id, task)
                    : Error(request.Id, JsonRpcResponse.Codes.TaskNotFound, "Task not found."),
                _ => Error(request.Id, JsonRpcResponse.Codes.MethodNotFound, $"Method '{request.Method}' is not supported.")
            };
        }
        catch (Exception ex) when (ex is JsonException or NullReferenceException or InvalidOperationException)
        {
            return Error(request.Id, JsonRpcResponse.Codes.InvalidParams, "Invalid params.");
        }
    }

    private async Task<A2ATask> HandleMessageAsync(MessageSendParams sendParams, CancellationToken cancellationToken)
    {
        var message = sendParams.Message;
        var taskId = Guid.NewGuid().ToString();
        var contextId = message.ContextId ?? Guid.NewGuid().ToString();

        // The calling agent names the skill it wants in message.metadata.skillId (taken from our Agent Card)
        var skillId = message.Metadata?.TryGetValue("skillId", out var skill) == true && skill.ValueKind == JsonValueKind.String
            ? skill.GetString()
            : null;
        var data = message.Parts.FirstOrDefault(p => p.Kind == "data")?.Data;

        logger.LogInformation("A2A task {TaskId}: skill {Skill} requested by {Sender}",
            taskId, skillId, message.Metadata?.GetValueOrDefault("senderAgent"));

        var (state, text, artifacts) = (skillId, data) switch
        {
            (AlertsAgentCard.TrackMedicineExpiry, { } d) => await TrackExpiryAsync(d, cancellationToken),
            (AlertsAgentCard.GetExpiringItems, var d) => await GetExpiringAsync(d, cancellationToken),
            (AlertsAgentCard.TrackMedicineExpiry, null) => (TaskState.Rejected, "A data part with the batch is required.", new List<A2AArtifact>()),
            _ => (TaskState.Rejected, $"Unknown skill '{skillId}'. See /.well-known/agent-card.json.", new List<A2AArtifact>())
        };

        var reply = new A2AMessage("agent", [Part.FromText(text)], Guid.NewGuid().ToString(), taskId, contextId);
        var task = new A2ATask(
            taskId,
            contextId,
            new A2ATaskStatus(state, reply, DateTime.UtcNow),
            artifacts,
            [message with { TaskId = taskId, ContextId = contextId }, reply]);

        taskStore.Save(task);
        return task;
    }

    private async Task<(string, string, List<A2AArtifact>)> TrackExpiryAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var batch = data.Deserialize<MedicineItemAddedEvent>(JsonSerializerOptions.Web);
        if (batch is null || batch.ItemId == Guid.Empty || batch.KitId == Guid.Empty || batch.ExpiryDate == default)
            return (TaskState.Rejected, "itemId, kitId and expiryDate are required.", []);

        var result = await tracker.RegisterAsync(batch, cancellationToken);
        var expiry = result.Batch.EffectiveExpiryDate;
        var classification = ExpiryTracker.Classify(expiry);
        var daysLeft = (int)Math.Floor((expiry - DateTime.UtcNow).TotalDays);

        // The consumer agent does not just store the event: it evaluates it and answers with a decision
        var text = classification switch
        {
            "Expired" => $"Batch {batch.ItemId} is already expired ({expiry:yyyy-MM-dd}). Do not use it.",
            "ExpiringSoon" => $"Batch {batch.ItemId} expires in {daysLeft} days. An alert will be raised.",
            _ => $"Batch {batch.ItemId} is tracked; expires in {daysLeft} days."
        };

        var artifact = new A2AArtifact(Guid.NewGuid().ToString(), "tracking-result",
        [
            Part.FromData(new
            {
                batchId = result.Batch.Id,
                itemId = batch.ItemId,
                expiryDate = expiry,
                daysLeft,
                classification,
                alreadyTracked = result.AlreadyTracked
            })
        ]);

        return (TaskState.Completed, text, [artifact]);
    }

    private async Task<(string, string, List<A2AArtifact>)> GetExpiringAsync(JsonElement? data, CancellationToken cancellationToken)
    {
        var days = data is { } d && d.TryGetProperty("days", out var p) && p.TryGetInt32(out var v) ? v : 30;
        if (days is < 0 or > 365)
            return (TaskState.Rejected, "days must be between 0 and 365.", []);

        var items = await mediator.Send(new GetExpiringItemsQuery(days), cancellationToken);
        var artifact = new A2AArtifact(Guid.NewGuid().ToString(), "expiring-items", [Part.FromData(items)]);
        return (TaskState.Completed, $"{items.Count} batch(es) expire within {days} days.", [artifact]);
    }

    private static JsonRpcResponse Error(JsonElement id, int code, string message) =>
        new(id, Error: new JsonRpcError(code, message));
}
