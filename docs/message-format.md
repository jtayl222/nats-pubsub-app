# NATS Message Format Specification

**Version:** 1.0
**Last Updated:** 2025-10-08

This document defines the standard message format for all NATS pub/sub implementations (Python, C#, Go, etc.).

## Overview

All messages published to NATS subjects must follow this JSON schema to ensure interoperability between different language implementations.

## Message Schema

### Published Message Structure

```json
{
  "message_id": "string",
  "timestamp": "string (ISO 8601)",
  "source": "string",
  "sequence": "number",
  "data": {
    "event_type": "string",
    "value": "number",
    "random_field": "string",
    "custom_data": "object (optional)"
  }
}
```

### Field Definitions

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `message_id` | string | Yes | Unique identifier: `{source}-{sequence}` |
| `timestamp` | string | Yes | ISO 8601 format with timezone: `2025-10-08T12:34:56.789Z` |
| `source` | string | Yes | Hostname or unique identifier of publisher |
| `sequence` | number | Yes | Monotonically increasing message counter |
| `data` | object | Yes | Message payload |
| `data.event_type` | string | Yes | Type of event (see Event Types below) |
| `data.value` | number | Yes | Numeric value associated with event |
| `data.random_field` | string | Yes | Test field with random value |
| `data.custom_data` | object | No | Additional custom fields |

## Event Types

Standard event types supported by all implementations:

| Event Type | Description | Example Value Range |
|------------|-------------|---------------------|
| `user.login` | User login event | 1-1000 |
| `user.logout` | User logout event | 1-1000 |
| `order.created` | New order created | 1-1000 |
| `payment.processed` | Payment transaction | 1-1000 |

Implementations may extend with additional event types.

## Example Messages

### User Login Event

```json
{
  "message_id": "nats-1-42",
  "timestamp": "2025-10-08T14:23:45.123Z",
  "source": "nats-1",
  "sequence": 42,
  "data": {
    "event_type": "user.login",
    "value": 523,
    "random_field": "alpha"
  }
}
```

### Order Created Event

```json
{
  "message_id": "nats-csharp-123",
  "timestamp": "2025-10-08T14:24:10.456Z",
  "source": "nats-csharp",
  "sequence": 123,
  "data": {
    "event_type": "order.created",
    "value": 789,
    "random_field": "beta",
    "custom_data": {
      "order_id": "ORD-12345",
      "amount": 99.99
    }
  }
}
```

## NATS Subject Convention

### Default Subject

```
events.test
```

### Subject Patterns

For more complex routing:

```
events.{event_type}         # events.user.login, events.order.created
events.{source}.{event_type} # events.nats-1.user.login
```

## Message Size Limits

- **Maximum message size**: 1 MB (1,048,576 bytes)
- **Recommended size**: < 64 KB for optimal performance
- **Typical message size**: ~200-300 bytes

## Timestamp Format

All timestamps must be in **ISO 8601 format** with UTC timezone:

```
YYYY-MM-DDTHH:mm:ss.sssZ
```

Examples:
- `2025-10-08T14:23:45.123Z`
- `2025-10-08T14:23:45.123456Z` (microseconds)

### Language-Specific Examples

**Python:**
```python
from datetime import datetime, timezone
timestamp = datetime.now(timezone.utc).isoformat()
```

**C#:**
```csharp
using System;
string timestamp = DateTime.UtcNow.ToString("o"); // ISO 8601
```

**Go:**
```go
import "time"
timestamp := time.Now().UTC().Format(time.RFC3339Nano)
```

## Message ID Format

Message IDs must follow the pattern: `{source}-{sequence}`

- `source`: Unique identifier of the publisher (hostname, pod name, etc.)
- `sequence`: Monotonically increasing integer starting from 1

Examples:
- `nats-1-42`
- `publisher-python-prod-1000`
- `nats-csharp-5`

## Validation Rules

Implementations should validate:

1. ✅ All required fields are present
2. ✅ `timestamp` is valid ISO 8601 format
3. ✅ `sequence` is a positive integer
4. ✅ `message_id` matches `{source}-{sequence}` pattern
5. ✅ `event_type` is one of the supported types
6. ✅ `value` is a number
7. ✅ Message size is under 1 MB

## Error Handling

### Invalid Messages

If a subscriber receives a message that doesn't match this schema:

1. Log error with details of validation failure
2. Increment error counter in metrics
3. Do not crash - continue processing other messages

### Missing Fields

- **Critical fields missing**: Log error, skip message
- **Optional fields missing**: Use default values or null

## Compatibility

### Version Compatibility

This message format is version 1.0. Future versions will:

- Maintain backward compatibility for required fields
- Add new optional fields without breaking existing implementations
- Deprecate fields with advance notice (minimum 6 months)

### Cross-Language Communication

All implementations (Python, C#, Go, etc.) must:

- ✅ Publish messages in this exact format
- ✅ Be able to consume messages from any other implementation
- ✅ Validate incoming messages against this schema
- ✅ Log warnings for unknown fields (for forward compatibility)

## Testing

### Test Messages

Use these test messages to verify implementation:

**Valid message:**
```json
{
  "message_id": "test-1",
  "timestamp": "2025-10-08T00:00:00.000Z",
  "source": "test",
  "sequence": 1,
  "data": {
    "event_type": "user.login",
    "value": 100,
    "random_field": "test"
  }
}
```

**Invalid messages** (for negative testing):
```json
// Missing required field
{"message_id": "test-1", "timestamp": "...", "source": "test"}

// Invalid timestamp format
{"message_id": "test-1", "timestamp": "2025-10-08", ...}

// Invalid sequence type
{"message_id": "test-1", "timestamp": "...", "sequence": "not-a-number", ...}
```

## Extensions

### Custom Fields

Implementations may add custom fields under `data.custom_data`:

```json
{
  "message_id": "nats-1-42",
  "timestamp": "2025-10-08T14:23:45.123Z",
  "source": "nats-1",
  "sequence": 42,
  "data": {
    "event_type": "order.created",
    "value": 789,
    "random_field": "beta",
    "custom_data": {
      "order_id": "ORD-12345",
      "user_id": "USR-67890",
      "metadata": {
        "ip_address": "192.168.1.100",
        "user_agent": "..."
      }
    }
  }
}
```

### Adding New Event Types

To add new event types:

1. Document in this file under "Event Types"
2. Update all implementations to recognize new type
3. Update tests to include new event type

## Schema Evolution

### Adding Optional Fields

New optional fields can be added at any time:

```json
{
  "message_id": "test-1",
  "timestamp": "2025-10-08T14:23:45.123Z",
  "source": "test",
  "sequence": 1,
  "version": "1.1",  // NEW optional field
  "data": {
    "event_type": "user.login",
    "value": 100,
    "random_field": "test",
    "priority": "high"  // NEW optional field
  }
}
```

### Deprecating Fields

When deprecating a field:

1. Mark as deprecated in this document
2. Continue accepting the field for 6 months
3. Log warning when deprecated field is used
4. Remove after deprecation period

## References

- [JSON Schema Specification](https://json-schema.org/)
- [ISO 8601 Timestamp Format](https://en.wikipedia.org/wiki/ISO_8601)
- [NATS Subject-Based Messaging](https://docs.nats.io/nats-concepts/subjects)
