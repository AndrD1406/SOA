using System.ComponentModel.DataAnnotations;

namespace MedStorage.User.Api.Registration;

// Validation attributes go on the constructor parameters: MVC rejects them on record properties
public record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9._-]+$")] string Username,
    [Required, EmailAddress] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password,
    // Keycloak's default user profile requires both names, otherwise the first login asks to complete the profile
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName);
