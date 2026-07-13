using System.ComponentModel.DataAnnotations;

namespace AspNetIdentitySample.Models;

// These are the wire contract between the React auth UI and the server. They are shaped for JSON and
// carry only data-annotation checks (required, email format, password confirmation match). The actual
// password policy (length, character classes, uniqueness) is enforced by UserManager, not restated
// here where it would drift. The server emits an OpenAPI document from these types at build time, and
// the client generates its TypeScript types from that document, so the two sides cannot silently drift.

/// <summary>The sign-in form's body.</summary>
public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>The OIDC request_uri that sent the user here, echoed back so the flow can resume.</summary>
    public string? RequestUri { get; set; }
}

/// <summary>The sign-up form's body.</summary>
public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password), ErrorMessage = "The password and its confirmation do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>The OIDC request_uri that sent the user here, echoed back so the flow can resume.</summary>
    public string? RequestUri { get; set; }
}

/// <summary>
/// A successful sign-in or sign-up. The client navigates to <see cref="RedirectUrl"/> to hand control
/// back to the OIDC authorization endpoint and complete the flow.
/// </summary>
public sealed class AuthSuccessResponse
{
    public required string RedirectUrl { get; set; }
}
