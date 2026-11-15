using MassTransit;

// Consumed by the Inventory Service (default kit), which keeps its own copy in the same namespace.
namespace MedStorage.Contracts;

[MessageUrn("medstorage:user:UserRegisteredEvent")]
[EntityName("medstorage.user-registered")]
public record UserRegisteredEvent(
    Guid UserId,
    string Username,
    string? Email,
    DateTime RegisteredAt);
