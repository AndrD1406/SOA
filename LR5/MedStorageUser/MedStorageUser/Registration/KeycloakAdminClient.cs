using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MedStorage.User.Api.Registration;

public class UserAlreadyExistsException(string username)
    : Exception($"User '{username}' or this e-mail is already registered.");

// Creates users through the Keycloak Admin REST API. The User Service authenticates as its own
// service account (client "user-service", role realm-management/manage-users) via client credentials.
public class KeycloakAdminClient(HttpClient httpClient, IConfiguration configuration)
{
    public async Task<Guid> CreateUserAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("KeycloakAdmin");
        var baseUrl = section["BaseUrl"]!.TrimEnd('/');
        var realm = section["Realm"];

        var token = await GetServiceTokenAsync(baseUrl, realm!, section, cancellationToken);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/admin/realms/{realm}/users")
        {
            Content = JsonContent.Create(new
            {
                username = request.Username,
                email = request.Email,
                firstName = request.FirstName,
                lastName = request.LastName,
                enabled = true,
                emailVerified = true, // no SMTP in the local setup
                credentials = new[] { new { type = "password", value = request.Password, temporary = false } }
            })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new UserAlreadyExistsException(request.Username);
        response.EnsureSuccessStatusCode();

        // 201 Created, Location: .../users/{id} - the id is the future "sub" claim of the user's tokens
        var location = response.Headers.Location
                       ?? throw new InvalidOperationException("Keycloak did not return the new user's location.");
        return Guid.Parse(location.Segments[^1]);
    }

    private async Task<string> GetServiceTokenAsync(string baseUrl, string realm, IConfigurationSection section, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"{baseUrl}/realms/{realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = section["ClientId"]!,
                ["client_secret"] = section["ClientSecret"]!
            }), cancellationToken);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        return token?.AccessToken ?? throw new InvalidOperationException("Empty token response from Keycloak.");
    }

    private record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
