using MassTransit;
using MedStorage.User.Api.Authorization;
using MedStorage.User.Api.Registration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. CORS: only the MedStorage web client may call this API from a browser.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("MedStorageCorsPolicy", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Registration: Keycloak Admin API client + RabbitMQ publisher for UserRegisteredEvent
builder.Services.AddHttpClient<KeycloakAdminClient>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
    });
});

// 2. JWT authentication against Keycloak (OIDC).
// Authority is where the service downloads the signing keys (inside Docker: http://keycloak:8080/...).
// ValidIssuer is the "iss" inside tokens, i.e. the URL clients used to log in (http://localhost:8080/...).
var jwt = builder.Configuration.GetSection("JwtAuth");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwt["Authority"];
        options.Audience = jwt["Audience"];
        options.RequireHttpsMetadata = jwt.GetValue<bool>("RequireHttpsMetadata");
        // Keep raw JWT claim names ("sub", "scope", "preferred_username")
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["ValidIssuer"], // null => issuer from the discovery document
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "preferred_username",
            RoleClaimType = "roles"
        };
    });

// 3. OAuth scopes -> authorization policies
builder.Services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.UserRead, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new ScopeRequirement("user.read")));

    options.AddPolicy(Policies.UserWrite, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new ScopeRequirement("user.write")));
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// UseCors must run before UseAuthentication/UseAuthorization so preflight OPTIONS requests are answered without a token.
app.UseCors("MedStorageCorsPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
