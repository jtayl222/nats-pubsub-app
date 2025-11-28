using System.Text.Json.Serialization;

namespace NatsHttpGateway.Models;

public class MessageResponse
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("sequence")]
    public ulong? Sequence { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("size_bytes")]
    public int SizeBytes { get; set; }
}

public class FetchMessagesResponse
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("messages")]
    public List<MessageResponse> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public string? Stream { get; set; }
}

public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "healthy";

    [JsonPropertyName("nats_connected")]
    public bool NatsConnected { get; set; }

    [JsonPropertyName("nats_url")]
    public string NatsUrl { get; set; } = string.Empty;

    [JsonPropertyName("jetstream_available")]
    public bool JetStreamAvailable { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
