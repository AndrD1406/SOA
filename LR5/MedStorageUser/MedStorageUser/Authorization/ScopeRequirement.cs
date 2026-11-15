using Microsoft.AspNetCore.Authorization;

namespace MedStorage.User.Api.Authorization;

/// <summary>
/// Requires an OAuth2 scope in the access token.
/// Keycloak issues scopes as ONE space-separated "scope" claim
/// (e.g. "openid profile email user.read"), so a plain
/// RequireClaim("scope", "user.read") would never match.
/// </summary>
public sealed class ScopeRequirement(string scope) : IAuthorizationRequirement
{
    public string Scope { get; } = scope;
}

public sealed class ScopeHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        var granted = context.User.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (granted.Contains(requirement.Scope, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class Policies
{
    public const string UserRead = "RequireUserReadScope";
    public const string UserWrite = "RequireUserWriteScope";
}
