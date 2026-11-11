using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Represents an admin user who can authenticate and manage company content.
///
/// Maps to the <c>admin_users</c> table.
///
/// Requirements 11.1: THE Database SHALL persist Admin user credentials.
/// Requirement 8.7: passwords are stored as bcrypt hashes — never plaintext.
/// </summary>
public sealed class AdminUser
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Login username — max 254 characters, must be unique.</summary>
    [Column("username")]
    [MaxLength(254)]
    public string Username { get; set; } = string.Empty;

    /// <summary>bcrypt hash of the admin's password.</summary>
    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Running count of consecutive failed login attempts since the last
    /// successful login. Resets to 0 on successful authentication.
    /// (Requirement 8.5)
    /// </summary>
    [Column("failed_login_attempts")]
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// UTC time at which a temporary account lockout expires.
    /// NULL when the account is not currently locked. (Requirement 8.5)
    /// </summary>
    [Column("locked_until")]
    public DateTimeOffset? LockedUntil { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
