namespace NatsHttpGateway.Models;

/// <summary>
/// Request to create a new consumer
/// </summary>
public class CreateConsumerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Durable { get; set; } = true; // true = durable (persistent), false = ephemeral (temporary)
    public string? FilterSubject { get; set; }
    public string DeliverPolicy { get; set; } = "all"; // all, last, new, by_start_sequence, by_start_time
    public ulong? StartSequence { get; set; }
    public DateTime? StartTime { get; set; }
    public string AckPolicy { get; set; } = "explicit"; // none, all, explicit
    public TimeSpan? AckWait { get; set; }
    public int? MaxDeliver { get; set; }
    public TimeSpan? InactiveThreshold { get; set; }
    public int? MaxAckPending { get; set; }
    public bool? FlowControl { get; set; }
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
