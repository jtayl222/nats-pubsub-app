namespace NatsHttpGateway.Configuration;

/// <summary>
/// Configuration options for JWT authentication.
/// Binds to "Jwt" section in appsettings.json or JWT_* environment variables.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The symmetric key used to validate JWT signatures.
    /// When null or empty, JWT authentication is disabled.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Expected JWT issuer. When null, issuer validation is skipped.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Expected JWT audience. When null, audience validation is skipped.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Returns true if JWT authentication is enabled (Key is configured).
    /// </summary>
    public bool IsEnabled => !string.IsNullOrEmpty(Key);
}
