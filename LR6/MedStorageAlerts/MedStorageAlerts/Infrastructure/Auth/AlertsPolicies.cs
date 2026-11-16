namespace MedStorage.Alerts.Api.Infrastructure.Auth;

public static class AlertsPolicies
{
    // Users reading their own expiry alerts
    public const string Read = "RequireAlertsRead";

    // Peer agents (service accounts) calling the A2A JSON-RPC endpoint
    public const string A2A = "RequireAlertsA2A";
}
