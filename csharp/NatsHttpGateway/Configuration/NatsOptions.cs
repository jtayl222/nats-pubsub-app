namespace NatsHttpGateway.Configuration;

/// <summary>
/// Configuration options for NATS connection.
/// Binds to "Nats" section in appsettings.json or NATS_* environment variables.
/// </summary>
public class NatsOptions
{
    public const string SectionName = "Nats";

    /// <summary>
    /// NATS server URL. Defaults to "nats://localhost:4222".
    /// Can also be set via NATS_URL environment variable.
    /// </summary>
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Default stream prefix for auto-created streams. Defaults to "events".
    /// Can also be set via STREAM_PREFIX environment variable.
    /// </summary>
    public string StreamPrefix { get; set; } = "events";

    /// <summary>
    /// Path to CA certificate file for TLS verification.
    /// Can also be set via NATS_CA_FILE environment variable.
    /// </summary>
    public string? CaFile { get; set; }

    /// <summary>
    /// Path to client certificate file for mTLS.
    /// Can also be set via NATS_CERT_FILE environment variable.
    /// </summary>
    public string? CertFile { get; set; }

    /// <summary>
    /// Path to client private key file for mTLS.
    /// Can also be set via NATS_KEY_FILE environment variable.
    /// </summary>
    public string? KeyFile { get; set; }

    /// <summary>
    /// Returns true if TLS is configured (at least CA file is provided).
    /// </summary>
    public bool IsTlsEnabled => !string.IsNullOrEmpty(CaFile);

    /// <summary>
    /// Returns true if mutual TLS (client certificate) is configured.
    /// </summary>
    public bool IsMtlsEnabled => !string.IsNullOrEmpty(CertFile) && !string.IsNullOrEmpty(KeyFile);
}
