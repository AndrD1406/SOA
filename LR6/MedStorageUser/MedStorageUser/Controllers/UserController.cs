using MassTransit;
using MedStorage.Contracts;
using MedStorage.User.Api.Authorization;
using MedStorage.User.Api.Registration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedStorage.User.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class UserController(KeycloakAdminClient keycloak, IPublishEndpoint publishEndpoint, ILogger<UserController> logger) : ControllerBase
{
    // Public sign-up: creates the account in Keycloak, then announces it to the other services
    // (Inventory creates a default kit). The caller logs in afterwards to get a token.
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        Guid userId;
        try
        {
            userId = await keycloak.CreateUserAsync(request, cancellationToken);
        }
        catch (UserAlreadyExistsException ex)
        {
            return Conflict(new { Error = ex.Message });
        }

        // No local database, so no transactional outbox: if RabbitMQ is down the account still exists
        // and the user can create a kit manually (POST /api/v1/inventory/kits).
        await publishEndpoint.Publish(
            new UserRegisteredEvent(userId, request.Username, request.Email, DateTime.UtcNow), cancellationToken);

        logger.LogInformation("User {Username} registered with id {UserId}", request.Username, userId);
        return Created((string?)null, new { Id = userId, request.Username, request.Email });
    }

    // Requires scope "user.read"
    [HttpGet("profile")]
    [Authorize(Policy = Policies.UserRead)]
    public IActionResult GetProfile()
    {
        return Ok(new
        {
            Id = User.FindFirst("sub")?.Value,
            Username = User.Identity?.Name,
            Email = User.FindFirst("email")?.Value,
            Scopes = User.FindFirst("scope")?.Value,
            Message = "Це захищений ресурс. Ви успішно пройшли автентифікацію та авторизацію за Scope!"
        });
    }

    // Requires scope "user.write"
    [HttpPost("profile/update")]
    [Authorize(Policy = Policies.UserWrite)]
    public IActionResult UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        return Ok(new
        {
            Id = User.FindFirst("sub")?.Value,
            request.FirstName,
            request.LastName,
            Message = "Профіль успішно оновлено!"
        });
    }
}

public record UpdateProfileRequest(string? FirstName, string? LastName);
