namespace MedStorage.Inventory.Api.Features.A2A;

// Agent Card of the Producer agent (LR4, Task 3.1), served at GET /.well-known/agent-card.json.
public static class InventoryAgentCard
{
    public const string PublishMedicineAdded = "publish_medicine_added";
    public const string GetKitInventory = "get_kit_inventory";

    public static AgentCard Build(string publicUrl) => new(
        ProtocolVersion: "0.3.0",
        Name: "MedStorage Inventory Agent",
        Description: "Producer agent of MedStorage. Owns home medicine kits and announces new medicine batches " +
                     "to downstream agents (expiry tracking) by delegating A2A tasks to them.",
        Url: $"{publicUrl.TrimEnd('/')}/a2a",
        PreferredTransport: "JSONRPC",
        Version: "1.0.0",
        Provider: new AgentProvider("MedStorage", "https://github.com/medstorage"),
        Capabilities: new AgentCapabilities(Streaming: false, PushNotifications: false, StateTransitionHistory: true),
        DefaultInputModes: ["application/json"],
        DefaultOutputModes: ["application/json", "text/plain"],
        Skills:
        [
            new AgentSkill(
                Id: PublishMedicineAdded,
                Name: "Publish medicine added",
                Description: "Announces a medicine batch that was added to a kit: discovers an agent with the " +
                             "track_medicine_expiry skill and delegates an A2A task to it. On success the batch " +
                             "becomes Active, on failure Failed (same saga states as the RabbitMQ flow). " +
                             "Input: data part {\"itemId\": \"<guid>\"}.",
                Tags: ["inventory", "producer", "medicine-item-added", "delegation"],
                Examples: ["{\"itemId\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\"}"],
                InputModes: ["application/json"],
                OutputModes: ["application/json", "text/plain"]),
            new AgentSkill(
                Id: GetKitInventory,
                Name: "Get kit inventory",
                Description: "Lists medicine batches of a kit. Input: data part {\"kitId\": \"<guid>\"}.",
                Tags: ["inventory", "kit", "report"],
                Examples: ["{\"kitId\":\"11111111-1111-1111-1111-111111111111\"}"],
                InputModes: ["application/json"],
                OutputModes: ["application/json"])
        ]);
}
