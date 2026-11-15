using MassTransit;

// Published by the User Service after a user registers; the User Service keeps its own copy of this contract.
namespace MedStorage.Contracts;

[MessageUrn("medstorage:user:UserRegisteredEvent")]
[EntityName("medstorage.user-registered")]
public record UserRegisteredEvent(
    Guid UserId,
    string Username,
    string? Email,
    DateTime RegisteredAt);
