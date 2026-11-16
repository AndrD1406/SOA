using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MedStorage.Inventory.Api.Features.ServiceAuth;

// Machine-to-machine identity (OAuth2 client credentials): the Inventory Service calls Catalog and the
// Alerts agent with ITS OWN token (client "inventory-service", scopes catalog:read and alerts:a2a),
// so the downstream services verify who is calling instead of trusting the Docker network.
public class ServiceTokenProvider(HttpClient httpClient, IConfiguration configuration, ILogger<ServiceTokenProvider> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && DateTime.UtcNow < _expiresAt)
            return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTime.UtcNow < _expiresAt)
                return _token;

            var section = configuration.GetSection("ServiceAuth");
            using var response = await httpClient.PostAsync(section["TokenEndpoint"], new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = section["ClientId"]!,
                    ["client_secret"] = section["ClientSecret"]!
                }), cancellationToken);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                        ?? throw new InvalidOperationException("Empty token response from Keycloak.");

            // Refresh 30 seconds early so a token never expires on the way to the downstream service
            _token = token.AccessToken;
            _expiresAt = DateTime.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 30, 0));
            logger.LogInformation("Obtained service token for {ClientId}, valid for {Seconds}s", section["ClientId"], token.ExpiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

// Adds "Authorization: Bearer <service token>" to every request of the HttpClient it is attached to
public class ServiceTokenHandler(ServiceTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}
