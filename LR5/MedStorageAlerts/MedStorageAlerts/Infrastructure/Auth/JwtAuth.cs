using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace MedStorage.Alerts.Api.Infrastructure.Auth;

// Zero Trust: every service validates the Keycloak JWT itself (signature, issuer, audience, lifetime)
// and authorizes each endpoint by OAuth scope, instead of trusting the gateway or the caller's network.
// Each microservice keeps its own copy of this file (no shared library between the services).
public static class JwtAuthExtensions
{
    public static IServiceCollection AddMedStorageJwtAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        params (string Policy, string Scope)[] scopePolicies)
    {
        var jwt = configuration.GetSection("JwtAuth");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Authority: where signing keys are downloaded from (inside Docker: http://keycloak:8080/realms/medstorage)
                options.Authority = jwt["Authority"];
                options.RequireHttpsMetadata = jwt.GetValue("RequireHttpsMetadata", false); // only for local development
                // Keep raw JWT claim names: "sub", "scope", "preferred_username"
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    // "iss" in tokens is the URL clients log in through (http://localhost:8080/realms/medstorage)
                    ValidIssuer = jwt["ValidIssuer"],
                    // Each API has its own audience, added by Keycloak when one of its scopes is granted
                    ValidateAudience = true,
                    ValidAudience = jwt["Audience"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "preferred_username"
                };
            });

        services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
        services.AddAuthorization(options =>
        {
            foreach (var (policy, scope) in scopePolicies)
            {
                options.AddPolicy(policy, p => p
                    .RequireAuthenticatedUser()
                    .AddRequirements(new ScopeRequirement(scope)));
            }
        });

        return services;
    }

    // "Authorize" button in Swagger UI: paste an access token to call protected endpoints
    public static IServiceCollection AddSwaggerWithBearer(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Keycloak access token (without the 'Bearer ' prefix)"
            });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
        return services;
    }

    // The user id always comes from the validated token ("sub"), never from the request body or URL
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id)
            ? id
            : throw new UnauthorizedAccessException("The access token has no valid 'sub' claim.");
    }
}

// Keycloak sends all scopes in ONE space-separated "scope" claim ("openid profile catalog:read"),
// so RequireClaim("scope", "catalog:read") would never match - the claim is split here instead.
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
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
