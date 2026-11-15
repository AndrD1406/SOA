namespace MedStorage.Alerts.Api.Features.A2A;

// Agent Card of the Consumer agent (LR4, Task 3.2).
// Any agent can discover what Alerts can do by reading GET /.well-known/agent-card.json:
// no central registry or broker is needed, the card itself is the contract.
public static class AlertsAgentCard
{
    public const string TrackMedicineExpiry = "track_medicine_expiry";
    public const string GetExpiringItems = "get_expiring_items";

    public static AgentCard Build(string publicUrl) => new(
        ProtocolVersion: "0.3.0",
        Name: "MedStorage Alerts Agent",
        Description: "Consumer agent of MedStorage. Tracks expiry dates of medicine batches in home kits " +
                     "and reports batches that are expired or about to expire.",
        Url: $"{publicUrl.TrimEnd('/')}/a2a",
        PreferredTransport: "JSONRPC",
        Version: "1.0.0",
        Provider: new AgentProvider("MedStorage", "https://github.com/medstorage"),
        Capabilities: new AgentCapabilities(Streaming: false, PushNotifications: false, StateTransitionHistory: true),
        DefaultInputModes: ["application/json", "text/plain"],
        DefaultOutputModes: ["application/json", "text/plain"],
        Skills:
        [
            new AgentSkill(
                Id: TrackMedicineExpiry,
                Name: "Track medicine expiry",
                Description: "Registers a newly added medicine batch for expiry monitoring and classifies it " +
                             "as Valid, ExpiringSoon (<= 30 days) or Expired. Input: a data part with " +
                             "itemId, kitId, drugId, quantity, expiryDate, addedAt (MedicineItemAddedEvent).",
                Tags: ["expiry", "tracking", "medicine-item-added", "consumer"],
                Examples: ["{\"itemId\":\"...\",\"kitId\":\"...\",\"drugId\":\"...\",\"quantity\":2,\"expiryDate\":\"2027-05-31\"}"],
                InputModes: ["application/json"],
                OutputModes: ["application/json", "text/plain"]),
            new AgentSkill(
                Id: GetExpiringItems,
                Name: "Get expiring items",
                Description: "Lists tracked batches that expire within the given number of days. Input: data part {\"days\": 30}.",
                Tags: ["expiry", "report"],
                Examples: ["{\"days\": 30}"],
                InputModes: ["application/json"],
                OutputModes: ["application/json"])
        ],
        SecuritySchemes: new()
        {
            ["keycloak"] = new AgentSecurityScheme("http", Scheme: "bearer", BearerFormat: "JWT",
                Description: "Keycloak access token (realm medstorage) with scope alerts:a2a")
        },
        Security: [new() { ["keycloak"] = ["alerts:a2a"] }]);
}
