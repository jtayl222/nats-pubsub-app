using System.Text.Json.Serialization;

namespace NatsHttpGateway.Models;

public class PublishRequest
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "http-gateway";
}

public class PublishResponse
{
    [JsonPropertyName("published")]
    public bool Published { get; set; }

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("stream")]
    public string Stream { get; set; } = string.Empty;

    [JsonPropertyName("sequence")]
    public ulong Sequence { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
