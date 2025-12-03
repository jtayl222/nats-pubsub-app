using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace NatsHttpGateway.Models;

/// <summary>
/// Request to create a new consumer
/// </summary>
/// <example>
/// {
///   "name": "order-processor",
///   "description": "Processes order events",
///   "durable": true,
///   "filterSubject": "events.orders.*",
///   "deliverPolicy": "all",
///   "ackPolicy": "explicit",
///   "ackWait": "00:00:30",
///   "maxDeliver": 3
/// }
/// </example>
public class CreateConsumerRequest
{
    /// <summary>
    /// Consumer name (required for durable consumers)
    /// </summary>
    /// <example>order-processor</example>
    [Required(ErrorMessage = "Consumer name is required")]
    [MinLength(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the consumer
    /// </summary>
    /// <example>Processes order events from the events stream</example>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the consumer is durable (persistent) or ephemeral (temporary). Durable consumers persist across restarts.
    /// </summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool Durable { get; set; } = true;

    /// <summary>
    /// Filter messages by subject pattern (e.g., "events.orders.*" or "events.>")
    /// </summary>
    /// <example>events.orders.*</example>
    public string? FilterSubject { get; set; }

    /// <summary>
    /// Delivery policy: "all" (all messages), "last" (last message), "new" (new messages only), "by_start_sequence", "by_start_time"
    /// </summary>
    /// <example>all</example>
    [DefaultValue("all")]
    public string DeliverPolicy { get; set; } = "all";

    /// <summary>
    /// Starting sequence number (required if deliverPolicy is "by_start_sequence")
    /// </summary>
    /// <example>100</example>
    public ulong? StartSequence { get; set; }

    /// <summary>
    /// Starting time (required if deliverPolicy is "by_start_time")
    /// </summary>
    /// <example>2025-01-01T00:00:00Z</example>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Acknowledgement policy: "none" (auto-ack), "all" (ack all), "explicit" (manual ack required)
    /// </summary>
    /// <example>explicit</example>
    [DefaultValue("explicit")]
    public string AckPolicy { get; set; } = "explicit";

    /// <summary>
    /// Time to wait for acknowledgement before redelivery. Format: "HH:MM:SS" or "d.HH:MM:SS" (e.g., "00:00:30" for 30 seconds)
    /// </summary>
    /// <example>00:00:30</example>
    public TimeSpan? AckWait { get; set; }

    /// <summary>
    /// Maximum number of delivery attempts before giving up (-1 for unlimited)
    /// </summary>
    /// <example>3</example>
    public int? MaxDeliver { get; set; }

    /// <summary>
    /// Time after which an inactive consumer is deleted. Format: "HH:MM:SS" or "d.HH:MM:SS". Defaults to 365 days for durable, 5 minutes for ephemeral.
    /// </summary>
    /// <example>1.00:00:00</example>
    public TimeSpan? InactiveThreshold { get; set; }

    /// <summary>
    /// Maximum pending acknowledgements allowed before pausing delivery
    /// </summary>
    /// <example>1000</example>
    public int? MaxAckPending { get; set; }

    /// <summary>
    /// Enable flow control for backpressure management
    /// </summary>
    /// <example>false</example>
    public bool? FlowControl { get; set; }

    /// <summary>
    /// Idle heartbeat interval. Format: "HH:MM:SS" (e.g., "00:00:30" for 30 seconds)
    /// </summary>
    /// <example>00:01:00</example>
    public TimeSpan? IdleHeartbeat { get; set; }
}

/// <summary>
/// Consumer summary for list operations
/// </summary>
public class ConsumerSummary
{
    public string StreamName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Created { get; set; }
    public ConsumerConfiguration Config { get; set; } = new();
    public ConsumerStateData State { get; set; } = new();
}

/// <summary>
/// Consumer configuration details
/// </summary>
public class ConsumerConfiguration
{
    public string? FilterSubject { get; set; }
    public string DeliverPolicy { get; set; } = string.Empty;
    public string AckPolicy { get; set; } = string.Empty;
    public TimeSpan? AckWait { get; set; }
    public int MaxDeliver { get; set; }
    public TimeSpan? InactiveThreshold { get; set; }
    public int MaxAckPending { get; set; }
    public bool FlowControl { get; set; }
    public TimeSpan? IdleHeartbeat { get; set; }
    public ulong? OptStartSeq { get; set; }
    public DateTime? OptStartTime { get; set; }
}

/// <summary>
/// Consumer state and metrics
/// </summary>
public class ConsumerStateData
{
    public ulong Delivered { get; set; }
    public ulong AckPending { get; set; }
    public ulong Redelivered { get; set; }
    public long NumPending { get; set; }
    public long NumWaiting { get; set; }
    public DateTime? LastDelivered { get; set; }
}

/// <summary>
/// Detailed consumer information including metrics
/// </summary>
public class ConsumerDetails
{
    public string StreamName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Created { get; set; }
    public ConsumerConfiguration Config { get; set; } = new();
    public ConsumerStateData State { get; set; } = new();
    public ConsumerMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Consumer performance metrics
/// </summary>
public class ConsumerMetrics
{
    public long ConsumerLag { get; set; } // Messages behind stream
    public long PendingMessages { get; set; } // Messages available to deliver
    public long AcknowledgedMessages { get; set; }
    public long RedeliveredMessages { get; set; }
    public double AverageAckTime { get; set; } // Average time to acknowledge
    public bool IsHealthy { get; set; }
    public string? HealthStatus { get; set; }
}

/// <summary>
/// Response for list consumers operation
/// </summary>
public class ConsumerListResult
{
    public string StreamName { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<ConsumerSummary> Consumers { get; set; } = new();
}

/// <summary>
/// Response for delete operation
/// </summary>
public class ConsumerDeleteResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Request to purge/reset consumer
/// </summary>
public class ConsumerPurgeRequest
{
    public string Action { get; set; } = "reset"; // reset, replay_from_sequence, replay_from_time
    public ulong? Sequence { get; set; }
    public DateTime? Time { get; set; }
}

/// <summary>
/// Consumer health check result
/// </summary>
public class ConsumerHealthResponse
{
    public string ConsumerName { get; set; } = string.Empty;
    public string StreamName { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastActivity { get; set; }
    public TimeSpan? TimeSinceLastActivity { get; set; }
    public long PendingMessages { get; set; }
    public long AckPending { get; set; }
    public string? Issue { get; set; }
}

/// <summary>
/// Request to peek messages from a consumer
/// </summary>
public class ConsumerPeekMessagesRequest
{
    public int Limit { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 5;
}

/// <summary>
/// Response containing peeked messages
/// </summary>
public class ConsumerPeekMessagesResponse
{
    public string ConsumerName { get; set; } = string.Empty;
    public string StreamName { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<MessagePreview> Messages { get; set; } = new();
}

/// <summary>
/// Preview of a message
/// </summary>
public class MessagePreview
{
    public ulong Sequence { get; set; }
    public string Subject { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
    public int SizeBytes { get; set; }
    public string? DataPreview { get; set; }
}

/// <summary>
/// Request to reset/purge consumer
/// </summary>
public class ConsumerResetRequest
{
    public string Action { get; set; } = "reset"; // reset, replay_from_sequence, replay_from_time
    public ulong? Sequence { get; set; }
    public DateTime? Time { get; set; }
}

/// <summary>
/// Response for reset operation
/// </summary>
public class ConsumerResetResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;
}

/// <summary>
/// Response for pause/resume operations
/// </summary>
public class ConsumerActionResponse
{
    public bool Success { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Request to create multiple consumers
/// </summary>
public class BulkCreateConsumersRequest
{
    public List<CreateConsumerRequest> Consumers { get; set; } = new();
}

/// <summary>
/// Response for bulk create operation
/// </summary>
public class BulkCreateConsumersResponse
{
    public int TotalRequested { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<ConsumerCreateResult> Results { get; set; } = new();
}

/// <summary>
/// Result of individual consumer creation
/// </summary>
public class ConsumerCreateResult
{
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ConsumerDetails? Consumer { get; set; }
}

/// <summary>
/// Consumer metrics history entry
/// </summary>
public class ConsumerMetricsSnapshot
{
    public DateTime Timestamp { get; set; }
    public long ConsumerLag { get; set; }
    public long PendingMessages { get; set; }
    public long AcknowledgedMessages { get; set; }
    public long RedeliveredMessages { get; set; }
    public ulong DeliveredCount { get; set; }
    public bool IsHealthy { get; set; }
}

/// <summary>
/// Response containing metrics history
/// </summary>
public class ConsumerMetricsHistoryResponse
{
    public string ConsumerName { get; set; } = string.Empty;
    public string StreamName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Count { get; set; }
    public List<ConsumerMetricsSnapshot> History { get; set; } = new();
}

/// <summary>
/// Consumer template definition
/// </summary>
public class ConsumerTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UseCase { get; set; } = string.Empty;
    public CreateConsumerRequest Template { get; set; } = new();
}

/// <summary>
/// Response containing available templates
/// </summary>
public class ConsumerTemplatesResponse
{
    public int Count { get; set; }
    public List<ConsumerTemplate> Templates { get; set; } = new();
}
