using System.Text.Json;
using System.Text.Json.Serialization;

// A2A (Agent-to-Agent) protocol data model, JSON-RPC 2.0 binding, protocol version 0.3.0.
// Each service keeps its own copy of these records (like the RabbitMQ contracts): agents share
// only the wire format, never a library, which keeps them independently deployable.
namespace MedStorage.Alerts.Api.Features.A2A;

// ---- Agent Card: published at /.well-known/agent-card.json for discovery ----
public record AgentCard(
    string ProtocolVersion,
    string Name,
    string Description,
    string Url,
    string PreferredTransport,
    string Version,
    AgentProvider Provider,
    AgentCapabilities Capabilities,
    string[] DefaultInputModes,
    string[] DefaultOutputModes,
    AgentSkill[] Skills);

public record AgentProvider(string Organization, string Url);

public record AgentCapabilities(bool Streaming, bool PushNotifications, bool StateTransitionHistory);

public record AgentSkill(
    string Id,
    string Name,
    string Description,
    string[] Tags,
    string[] Examples,
    string[] InputModes,
    string[] OutputModes);

// ---- Messages, parts, tasks ----
public record Part(
    string Kind, // "text" | "data"
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Data = null)
{
    public static Part FromText(string text) => new("text", Text: text);
    public static Part FromData(object data) => new("data", Data: JsonSerializer.SerializeToElement(data, JsonSerializerOptions.Web));
}

public record A2AMessage(
    string Role, // "user" (the calling agent) | "agent" (the remote agent)
    List<Part> Parts,
    string MessageId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TaskId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContextId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, JsonElement>? Metadata = null)
{
    public string Kind => "message";
}

public record MessageSendParams(A2AMessage Message);

public record TaskQueryParams(string Id);

public static class TaskState
{
    public const string Submitted = "submitted";
    public const string Working = "working";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
}

public record A2ATaskStatus(string State, A2AMessage? Message, DateTime Timestamp);

public record A2AArtifact(string ArtifactId, string Name, List<Part> Parts);

public record A2ATask(string Id, string ContextId, A2ATaskStatus Status, List<A2AArtifact> Artifacts, List<A2AMessage> History)
{
    public string Kind => "task";
}

// ---- JSON-RPC 2.0 envelope ----
public record JsonRpcRequest(string Jsonrpc, JsonElement Id, string Method, JsonElement Params);

public record JsonRpcError(int Code, string Message);

public record JsonRpcResponse(
    JsonElement Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Result = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonRpcError? Error = null)
{
    public string Jsonrpc => "2.0";

    public static class Codes
    {
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int TaskNotFound = -32001;
    }
}
