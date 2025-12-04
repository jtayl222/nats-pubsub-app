# NATS JetStream Consumers: Background & Concepts

This document provides background information on NATS JetStream consumers, terminology, and how they compare to other messaging systems.

## Table of Contents
- [Introduction: Getting Data from NATS](#introduction-getting-data-from-nats)
- [Core NATS vs JetStream](#core-nats-vs-jetstream)
- [Terminology: Consumers vs Subscribers](#terminology-consumers-vs-subscribers)
- [Mapping to Other Messaging Systems](#mapping-to-other-messaging-systems)
- [CLI to HTTP Mapping](#cli-to-http-mapping)
- [Consumer Types: Ephemeral vs Durable](#consumer-types-ephemeral-vs-durable)
- [Stream Naming & Casing](#stream-naming--casing)
- [Creating a Durable Consumer](#creating-a-durable-consumer)
- [Use Cases](#use-cases)
- [Consumer Configuration Options](#consumer-configuration-options)
- [Monitoring and Management](#monitoring-and-management)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)

## Introduction: Getting Data from NATS

NATS provides two distinct paradigms for consuming messages:

1.  **Core NATS (Basic Pub/Sub)** - Real-time, fire-and-forget messaging with no persistence.
2.  **JetStream** - Stream-based messaging with persistence, replay, and delivery guarantees.

**This gateway focuses exclusively on JetStream** because it provides HTTP access to persisted message streams, making it ideal for web applications and microservices that need reliable message delivery.

## Core NATS vs JetStream

### Core NATS (Subscribers)

In Core NATS, clients create **subscribers** that receive messages in real-time as they're published.

**Characteristics:**
- **No persistence**: Messages exist only in memory. If no subscriber is listening, the message is lost.
- **Push-based**: The server pushes messages to active subscribers immediately.
- **Stateless**: The server doesn't track delivery or acknowledgement.
- **Not available via this HTTP gateway**: HTTP's request/response nature is incompatible with the persistent connections required for Core NATS.

### JetStream (Consumers)

JetStream adds **streams** (persistent message storage) and **consumers** (stateful message readers).

**Characteristics:**
- **Persistent storage**: Messages are stored on disk in streams.
- **Pull or push**: Consumers can pull messages on-demand or receive pushes.
- **Replay capable**: Consumers can retrieve historical messages.
- **Stateful**: The server tracks which messages have been delivered and acknowledged.
- **Delivery guarantees**: Supports at-least-once or exactly-once semantics.

**JetStream is ideal for HTTP gateways** because it allows stateless HTTP requests to retrieve messages at any time, with full control over message delivery and acknowledgement.

## Terminology: Consumers vs Subscribers

- **Subscriber (Core NATS)**: A lightweight, stateless client connection that receives real-time messages. It must be actively connected.
- **Consumer (JetStream)**: A durable, server-side entity that tracks message delivery state. It can be ephemeral (temporary) or durable (persistent).

**Key difference**: A subscriber is just a client receiving messages. A consumer is a server-side object with its own configuration, state, and delivery tracking.

## Mapping to Other Messaging Systems

### Kafka → NATS Mapping

| Kafka Concept      | NATS Equivalent                           |
| ------------------ | ----------------------------------------- |
| **Topic**          | **Stream** (JetStream)                    |
| **Partition**      | (No direct equivalent; use multiple streams) |
| **Consumer Group** | **Durable Consumer** (JetStream)          |
| **Offset**         | **Sequence Number**                       |
| **Commit**         | **Ack** (Acknowledgement)                 |

### RabbitMQ → NATS Mapping

| RabbitMQ Concept | NATS Equivalent         |
| ---------------- | ----------------------- |
| **Exchange**     | **Subject hierarchy**   |
| **Queue**        | **Consumer** (JetStream)|
| **Binding**      | **FilterSubject**       |

## CLI to HTTP Mapping

Use this table to translate familiar NATS CLI flows into the gateway's REST endpoints.

| Intent | NATS CLI | HTTP Gateway |
|--------|----------|--------------|
| Check gateway + JetStream health | `nats server check` / `nats account info` | `GET /Health` |
| Inspect a stream | `nats stream info events` | `GET /api/Streams/{stream}` |
| Publish JSON message | `nats pub events.test '{"foo":1}'` | `POST /api/messages/{subject}` |
| Publish protobuf message | `nats pub --raw events.test < bytes` | `POST /api/proto/protobufmessages/{subject}` |
| Create consumer | `nats consumer add events demo` | `POST /api/consumers/{stream}` |
| List consumers | `nats consumer ls events` | `GET /api/consumers/{stream}` |
| Inspect consumer | `nats consumer info events demo` | `GET /api/consumers/{stream}/{consumer}` |
| Fetch via durable consumer | `nats consumer next events demo --count 10` | `GET /api/messages/{stream}/consumer/{consumer}?limit=10` |
| Peek without advancing | `nats consumer next --peek` | `GET /api/consumers/{stream}/{consumer}/messages?limit=10` |
| Reset consumer cursor | `nats consumer edit --deliver all` | `POST /api/consumers/{stream}/{consumer}/reset` |
| Delete consumer | `nats consumer rm events demo` | `DELETE /api/consumers/{stream}/{consumer}` |
| Retrieve templates | (n/a) | `GET /api/consumers/templates` |

## Consumer Types: Ephemeral vs Durable

### Ephemeral Consumers

- **Lifespan**: Created on-demand for a single request and deleted immediately after use.
- **State**: No state persistence. Each request is independent.
- **Position**: Always fetches the *last N* messages from the stream.
- **Use Case**: Quick lookups, debugging, or stateless read-only queries.

### Durable Consumers

- **Lifespan**: Persists indefinitely until explicitly deleted.
- **State**: Maintains its position (the last delivered message sequence).
- **Position**: Configurable. Can start from the beginning, end, a specific sequence, or a point in time.
- **Use Case**: Stateful message processing workflows, event-driven systems, and background jobs where you need to track progress.

## Stream Naming & Casing

JetStream stream names are **case-sensitive**, and the CLI traditionally uppercases input unless you explicitly quote it. The gateway preserves whatever casing you configure in JetStream. To avoid `404 stream not found` errors when mixing tools:

- Decide on a canonical casing upfront (e.g., `EVENTS_AUDIT` or `events.audit`).
- When using the NATS CLI, wrap stream names in quotes (`"events.audit"`) to prevent auto-uppercasing.
- In HTTP calls, ensure the `stream` route parameter matches the exact casing reported by `GET /api/Streams/{stream}` or `nats stream ls`.
- Stream prefixes configured via `STREAM_PREFIX` also participate in casing; confirm your appsettings vs CLI commands match.

## Creating a Durable Consumer

Before using a durable consumer, you must create it. The recommended method is the **NATS CLI**.

1.  **Install the NATS CLI:**
    ```bash
    # macOS
    brew install nats-io/nats-tools/nats

    # Linux/Windows: Download from https://github.com/nats-io/natscli/releases
    ```

2.  **Create a consumer:**
    ```bash
    # Basic consumer that reads all messages from the 'events.test' subject
    nats consumer add events my-service-consumer \
      --filter events.test \
      --deliver all \
      --ack none

    # Consumer for a reliable workflow
    nats consumer add events payment-processor \
      --filter events.payment.> \
      --ack explicit \
      --max-deliver 5 \
      --deliver all
    ```

3.  **Verify creation:**
    ```bash
    nats consumer ls events
    nats consumer info events my-service-consumer
    ```

Alternatively, you can create consumers programmatically or via the `POST /api/consumers/{stream}` endpoint provided by this gateway.

## Use Cases

### When to Use Ephemeral Consumers

✅ **Quick lookups**: "Show me the last 10 events."
✅ **Debugging**: Inspecting recent messages on a stream.
✅ **Dashboard displays**: Showing a real-time view of recent activity.
✅ **Stateless services**: When each request is independent and doesn't need to track position.

### When to Use Durable Consumers

✅ **Message processing workflows**: Processing each message exactly once.
✅ **Event-driven systems**: Reacting to events in the order they occurred.
✅ **Background jobs**: Consuming and processing a large backlog of messages over time.
✅ **Guaranteed delivery**: Using acknowledgment policies to ensure messages are processed reliably.

## Consumer Configuration Options

When creating durable consumers, you have full control over their behavior.

### Deliver Policy
| Policy | Description | Use Case |
|---|---|---|
| `all` | Start from the beginning of the stream. | Process entire history. |
| `last` | Start from the last message. | Process only new events. |
| `new` | Start with messages created after the consumer. | Real-time processing only. |
| `by_start_sequence` | Start from a specific sequence number. | Resume from a known point. |
| `by_start_time` | Start from a specific timestamp. | Process events from a certain time. |

### Ack Policy
| Policy | Description | Use Case |
|---|---|---|
| `none` | No acknowledgment needed (fire-and-forget). | Read-only, no delivery guarantees. |
| `explicit` | Each message must be individually acknowledged. | Guaranteed, reliable processing. |
| `all` | Acknowledging one message acknowledges all previous ones. | High-throughput batch processing. |

## Consumer Templates & Patterns

The gateway provides predefined consumer templates for common use cases. These templates are available via `GET /api/consumers/templates` and can be used as starting points for creating your own consumers.

### Real-Time Processor (Ephemeral)

**Use Case**: Process new messages as they arrive in real-time, such as event processing or real-time analytics.

**Characteristics**:
- Ephemeral (temporary) - auto-deleted after inactivity
- Processes only new messages (created after consumer starts)
- Explicit acknowledgment for reliability
- Limited retries (3 attempts)

**Create via CLI**:
```bash
nats consumer add events real-time-processor \
  --filter events.> \
  --deliver new \
  --ack explicit \
  --max-deliver 3 \
  --ack-wait 30s
```

**Create via HTTP**:
```bash
curl -X POST "http://localhost:8080/api/consumers/events" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "real-time-processor",
    "durable": false,
    "filterSubject": "events.>",
    "deliverPolicy": "new",
    "ackPolicy": "explicit",
    "maxDeliver": 3,
    "ackWait": "00:00:30"
  }'
```

**When NOT to use**: Don't use ephemeral consumers if you need to process historical messages or maintain state across restarts.

---

### Batch Processor (Durable)

**Use Case**: Process all messages from the beginning, ideal for batch processing, data migration, or building materialized views.

**Characteristics**:
- Durable (persistent) - survives restarts
- Processes all messages from stream start
- Explicit acknowledgment
- Higher retry limit (5 attempts)
- Longer acknowledgment timeout (5 minutes)

**Create via CLI**:
```bash
nats consumer add events batch-processor \
  --filter events.> \
  --deliver all \
  --ack explicit \
  --max-deliver 5 \
  --ack-wait 5m
```

**Create via HTTP**:
```bash
curl -X POST "http://localhost:8080/api/consumers/events" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "batch-processor",
    "durable": true,
    "filterSubject": "events.>",
    "deliverPolicy": "all",
    "ackPolicy": "explicit",
    "maxDeliver": 5,
    "ackWait": "00:05:00"
  }'
```

**When NOT to use**: Don't use for low-latency real-time processing where you only care about recent events.

---

### Work Queue (Durable)

**Use Case**: Distribute tasks across multiple workers, such as job processing or task distribution.

**Characteristics**:
- Durable for reliable job processing
- Processes all messages
- Explicit acknowledgment
- High retry limit (10 attempts)
- Limits concurrent unacknowledged messages (100)
- Multiple workers can share the same consumer

**Create via CLI**:
```bash
nats consumer add events work-queue \
  --filter events.jobs.> \
  --deliver all \
  --ack explicit \
  --max-deliver 10 \
  --ack-wait 1m \
  --max-ack-pending 100
```

**Create via HTTP**:
```bash
curl -X POST "http://localhost:8080/api/consumers/events" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "work-queue",
    "durable": true,
    "filterSubject": "events.jobs.>",
    "deliverPolicy": "all",
    "ackPolicy": "explicit",
    "maxDeliver": 10,
    "ackWait": "00:01:00",
    "maxAckPending": 100
  }'
```

**Horizontal Scaling**: Multiple instances can bind to the same consumer name - NATS will distribute messages among them automatically.

---

### Fire-and-Forget (Ephemeral)

**Use Case**: Non-critical events like logging, metrics, or monitoring where message loss is acceptable.

**Characteristics**:
- Ephemeral (temporary)
- No acknowledgments (maximum performance)
- Only new messages
- Single delivery attempt
- Ideal for high-throughput observability data

**Create via CLI**:
```bash
nats consumer add events fire-and-forget \
  --filter events.metrics.> \
  --deliver new \
  --ack none \
  --max-deliver 1
```

**Create via HTTP**:
```bash
curl -X POST "http://localhost:8080/api/consumers/events" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "fire-and-forget",
    "durable": false,
    "filterSubject": "events.metrics.>",
    "deliverPolicy": "new",
    "ackPolicy": "none",
    "maxDeliver": 1
  }'
```

**When NOT to use**: Never use for critical business data or transactions where you cannot afford to lose messages.

---

### Latest-Only (Ephemeral)

**Use Case**: Only process the most recent message, useful for status updates or latest state snapshots.

**Characteristics**:
- Ephemeral
- Starts from the last message
- Skips historical data
- Explicit acknowledgment

**Create via CLI**:
```bash
nats consumer add events latest-only \
  --filter events.status.> \
  --deliver last \
  --ack explicit \
  --max-deliver 3
```

**Create via HTTP**:
```bash
curl -X POST "http://localhost:8080/api/consumers/events" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "latest-only",
    "durable": false,
    "filterSubject": "events.status.>",
    "deliverPolicy": "last",
    "ackPolicy": "explicit",
    "maxDeliver": 3
  }'
```

**Trade-off**: You'll miss any messages that arrived between the last message and when the consumer was created.

---

### Durable Processor (Critical Workloads)

**Use Case**: Mission-critical processing where no message can be lost and the consumer must survive restarts.

**Characteristics**:
- Durable with long inactivity threshold (24 hours)
- Unlimited retries (`maxDeliver: -1`)
- Long acknowledgment timeout (10 minutes)
- High max pending acknowledgments (1000)
- Processes all messages from beginning

**Create via CLI**:
```bash
nats consumer add events durable-processor \
  --filter events.critical.> \
  --deliver all \
  --ack explicit \
  --max-deliver -1 \
  --ack-wait 10m \
  --max-ack-pending 1000
```

**Create via HTTP**:
```bash
curl -X POST "http://localhost:8080/api/consumers/events" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "durable-processor",
    "durable": true,
    "filterSubject": "events.critical.>",
    "deliverPolicy": "all",
    "ackPolicy": "explicit",
    "maxDeliver": -1,
    "ackWait": "00:10:00",
    "inactiveThreshold": "1.00:00:00",
    "maxAckPending": 1000
  }'
```

**Warning**: Unlimited retries means poisoned messages will retry forever. Implement dead-letter queue handling in your application.

---

### Choosing the Right Template

| Scenario | Recommended Template | Key Reason |
|----------|---------------------|------------|
| Dashboard showing live events | Real-Time Processor | Only new messages, low latency |
| Processing order history | Batch Processor | All historical messages |
| Background job queue | Work Queue | Horizontal scaling, retry logic |
| Collecting application logs | Fire-and-Forget | High throughput, loss acceptable |
| Displaying current system status | Latest-Only | Only care about newest state |
| Payment processing | Durable Processor | Cannot lose transactions |
| Rebuilding cache from events | Batch Processor | Replay entire event stream |
| Real-time notifications | Real-Time Processor | Only new events matter |

## Resetting and Replaying Consumers

Sometimes you need to "rewind" a consumer to replay messages from a different starting point. This is essential for recovering from bugs, reprocessing data, or debugging issues.

### How Consumer Reset Works

**IMPORTANT**: Resetting a consumer is a **destructive operation**. The gateway doesn't actually "reset" the consumer state - instead, it:

1. **Retrieves** the current consumer configuration
2. **Deletes** the consumer entirely (including all state)
3. **Recreates** the consumer with the same name but a new starting position

This means:
- ❌ Any unacknowledged messages are lost
- ❌ Consumer metrics reset to zero
- ❌ Connected workers are disconnected
- ✅ Configuration (ack policy, retries, etc.) is preserved
- ✅ Consumer name remains the same

### The Three Reset Actions

#### 1. Reset to Beginning (`action: "reset"`)

Replays **all messages** from the first message in the stream.

**CLI Equivalent:**
```bash
nats consumer info events my-consumer
nats consumer rm events my-consumer
nats consumer add events my-consumer \
  --filter events.> \
  --deliver all \
  --ack explicit
```

**HTTP API:**
```bash
curl -X POST "http://localhost:8080/api/consumers/events/my-consumer/reset" \
  -H "Content-Type: application/json" \
  -d '{"action": "reset"}'
```

**Use Cases:**
- Bug fix: "My processor had a critical bug, I need to reprocess all events"
- Disaster recovery: "Database corrupted, rebuild from event stream"
- New feature: "Added a new projection, need to backfill from history"

**Example:**
```
Before reset:
  Stream messages: [1] [2] [3] [4] [5] [6] [7] [8] [9] [10] [11] [12]
  Consumer position:                              ^
                                             (at seq 8, delivered 8)

After reset (action="reset"):
  Consumer position: ^
                  (seq 1, will deliver all 12 messages)
```

---

#### 2. Replay from Sequence (`action: "replay_from_sequence"`)

Starts from a **specific message number** and continues forward.

**CLI Equivalent:**
```bash
nats consumer rm events my-consumer
nats consumer add events my-consumer \
  --filter events.> \
  --deliver by_start_sequence \
  --opt-start-seq 75 \
  --ack explicit
```

**HTTP API:**
```bash
curl -X POST "http://localhost:8080/api/consumers/events/my-consumer/reset" \
  -H "Content-Type: application/json" \
  -d '{
    "action": "replay_from_sequence",
    "sequence": 75
  }'
```

**Use Cases:**
- Partial reprocessing: "Messages 1-74 are fine, but 75-100 need reprocessing"
- Resume after manual intervention: "Support team fixed data, continue from message 200"
- Skip problematic messages: "Message 50 is poison, skip to 51"

**Example:**
```
Before reset:
  Stream messages: [1] [2] [3] [4] [5] [6] [7] [8] [9] [10] [11] [12]
  Consumer position:                              ^
                                             (at seq 8)

After reset (sequence=5):
  Consumer position:                 ^
                                (seq 5, will deliver 5-12)
```

**Precision:** This is the most predictable reset method because you know exactly which message you'll start from.

---

#### 3. Replay from Time (`action: "replay_from_time"`)

Starts from the **first message on or after** a specific timestamp.

**CLI Equivalent:**
```bash
nats consumer rm events my-consumer
nats consumer add events my-consumer \
  --filter events.> \
  --deliver by_start_time \
  --opt-start-time "2025-12-03T20:00:00Z" \
  --ack explicit
```

**HTTP API:**
```bash
curl -X POST "http://localhost:8080/api/consumers/events/my-consumer/reset" \
  -H "Content-Type: application/json" \
  -d '{
    "action": "replay_from_time",
    "time": "2025-12-03T20:00:00Z"
  }'
```

**Use Cases:**
- Time-based recovery: "Deployed buggy code at 8pm, reprocess since then"
- Regulatory requirements: "Audit requested replay of last 24 hours"
- Investigation: "Issue reported at 3pm, replay events since 2:50pm"

**Example:**
```
Stream messages with timestamps:
  [1: 19:45] [2: 19:50] [3: 19:55] [4: 20:00] [5: 20:05] [6: 20:10]

Reset to time="2025-12-03T20:00:00Z":
  Consumer starts at:                ^
                                (seq 4, first message >= 20:00)
```

**Warning:** Time-based reset is less precise than sequence-based because:
- Messages might arrive out of order
- Clock skew between publishers
- Timestamps are set when messages are published, not received

---

### Critical Warnings ⚠️

#### 1. **Unacknowledged Messages Are Lost**

If your consumer has delivered messages waiting for acknowledgment, they're **permanently lost** when you reset:

```
Before reset:
  Delivered: 100 messages
  AckPending: 10 (messages 91-100 waiting for ACK)

After reset:
  Those 10 unacked messages? GONE. Consumer is brand new.
```

**Mitigation:** Check consumer state before resetting:
```bash
curl "http://localhost:8080/api/consumers/events/my-consumer"
# Look at "ackPending" - if non-zero, wait for workers to finish
```

#### 2. **Concurrent Workers Are Disconnected**

If multiple instances are consuming from the same consumer, they'll all be disconnected when the consumer is deleted.

**Mitigation:**
1. Stop all workers
2. Perform reset
3. Restart workers

#### 3. **No Validation**

The reset operation doesn't validate:
- Whether the sequence number exists
- Whether the timestamp is reasonable
- How many messages you're about to replay

**Danger Example:**
```bash
# Stream has 10 MILLION messages
# Consumer is at message 9,999,999
# Accidental reset:
curl -X POST ".../reset" -d '{"action": "reset"}'
# Now consumer will replay ALL 10 MILLION messages!
```

**Mitigation:** Always check stream state first:
```bash
curl "http://localhost:8080/api/streams/events"
# Check "messages", "firstSeq", "lastSeq" before resetting
```

#### 4. **State Resets to Zero**

After reset, consumer metrics return to initial state:
- `delivered`: 0
- `ackPending`: 0
- `redelivered`: 0
- `created`: NOW (new timestamp)

This can break monitoring/alerting that tracks consumer progress.

---

### Best Practices

#### Before Resetting

1. **Verify current state:**
   ```bash
   # Check where consumer currently is
   GET /api/consumers/events/my-consumer
   ```

2. **Check stream bounds:**
   ```bash
   # Verify sequence/time ranges
   GET /api/streams/events
   ```

3. **Stop consuming:**
   - Gracefully shutdown all workers
   - Verify `ackPending` is zero

#### During Reset

4. **Use sequence-based for precision:**
   - More predictable than time-based
   - You know exactly which message starts
   - No timezone or clock skew issues

5. **Document why you're resetting:**
   ```bash
   # Good: Leave an audit trail
   echo "Resetting my-consumer due to bug #1234" >> reset_log.txt
   curl -X POST ".../reset" -d '{"action":"reset"}'
   ```

#### After Reset

6. **Verify new state:**
   ```bash
   # Confirm consumer was recreated
   GET /api/consumers/events/my-consumer
   # Check numPending matches expectations
   ```

7. **Monitor replay progress:**
   - Watch `delivered` count
   - Check for errors/failures
   - Ensure workers don't fall behind

---

### Alternative: Create a New Consumer Instead

Instead of resetting an existing consumer, consider creating a **new consumer** for testing or parallel processing:

```bash
# Don't reset production consumer
# Instead, create a test consumer
POST /api/consumers/events
{
  "name": "my-consumer-replay-test",
  "durable": true,
  "filterSubject": "events.>",
  "deliverPolicy": "all",
  "ackPolicy": "explicit"
}
```

**Advantages:**
- ✅ Production consumer keeps working
- ✅ No risk of breaking active workers
- ✅ Can compare results between old and new
- ✅ Easy rollback (just delete new consumer)

**When to use:**
- Testing reprocessing logic
- Parallel processing for backfill
- Investigating issues without impacting production

---

### Real-World Examples

#### Example 1: Bug Fix and Reprocess

**Scenario:** Deployed v2.0 with a bug that wrote incorrect totals to database.

```bash
# 1. Stop the buggy v2.0 workers
kubectl scale deployment order-processor --replicas=0

# 2. Verify consumer is idle
curl "http://localhost:8080/api/consumers/events/order-processor"
# Confirm ackPending = 0

# 3. Deploy v2.1 with the fix

# 4. Reset consumer to reprocess last 6 hours
curl -X POST "http://localhost:8080/api/consumers/events/order-processor/reset" \
  -H "Content-Type: application/json" \
  -d '{
    "action": "replay_from_time",
    "time": "2025-12-03T15:00:00Z"
  }'

# 5. Start v2.1 workers
kubectl scale deployment order-processor --replicas=3

# 6. Monitor progress
watch curl "http://localhost:8080/api/consumers/events/order-processor"
```

#### Example 2: Partial Reprocessing After Manual Fix

**Scenario:** Support team manually corrected data for orders 1000-1500. Need to reprocess from 1501 onwards.

```bash
# 1. Check current consumer position
curl "http://localhost:8080/api/consumers/events/order-processor"
# Response shows delivered=1500, numPending=2500

# 2. Reset to skip manually fixed range
curl -X POST "http://localhost:8080/api/consumers/events/order-processor/reset" \
  -H "Content-Type: application/json" \
  -d '{
    "action": "replay_from_sequence",
    "sequence": 1501
  }'

# 3. Verify new position
curl "http://localhost:8080/api/consumers/events/order-processor"
# Response shows delivered=0, numPending=2500 (sequences 1501-4000)
```

#### Example 3: Disaster Recovery

**Scenario:** Database was corrupted. Need to rebuild from event stream.

```bash
# 1. Create a new consumer for rebuild (don't affect production)
curl -X POST "http://localhost:8080/api/consumers/events" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "rebuild-consumer",
    "durable": true,
    "filterSubject": "orders.>",
    "deliverPolicy": "all",
    "ackPolicy": "explicit",
    "maxDeliver": -1,
    "ackWait": "00:10:00"
  }'

# 2. Start rebuild workers pointing to new consumer
./rebuild-worker --consumer=rebuild-consumer

# 3. Monitor progress
curl "http://localhost:8080/api/consumers/events/rebuild-consumer"

# 4. When complete, delete rebuild consumer
curl -X DELETE "http://localhost:8080/api/consumers/events/rebuild-consumer"
```

---

### Summary

| Aspect | Details |
|--------|---------|
| **What it does** | Deletes consumer, recreates with new starting position |
| **What's preserved** | Name, config (ack policy, retries, filters) |
| **What's lost** | State (delivered count, ack pending), unacked messages |
| **Three actions** | `reset` (all), `replay_from_sequence` (from seq), `replay_from_time` (from time) |
| **Best for precision** | `replay_from_sequence` |
| **Most dangerous** | Resetting production consumer with active workers |
| **Safer alternative** | Create new consumer for replay instead of resetting |
| **Check first** | `ackPending`, stream bounds, worker status |

## Monitoring and Management

You can use the NATS CLI or the gateway's API endpoints (`GET /api/consumers/{stream}/{name}`) to monitor consumer status.

**Key Metrics to Watch:**
- `Last Delivered Sequence`: The consumer's current position in the stream.
- `Num Pending`: The number of messages in the stream not yet delivered to the consumer (lag).
- `Num Ack Pending`: The number of messages delivered but not yet acknowledged.

## Best Practices

### For Ephemeral Consumers
- Use for **read-only operations** like inspecting or monitoring.
- Keep `limit` parameters reasonable to avoid fetching excessive data.

### For Durable Consumers
- **Name consumers clearly** based on their function (e.g., `payment-processor`).
- Use `ack: explicit` for any critical processing workflow.
- Set a `max-deliver` count to prevent infinite redelivery loops for failing messages.
- **Monitor consumer lag** (`Num Pending`) to detect if your processors are falling behind.
- Clean up unused consumers to free up server resources.

## Troubleshooting

### Problem: Consumer keeps re-reading the same messages.

**Cause**: The consumer's `AckPolicy` is likely `none`, or your application is not acknowledging messages when using `explicit` ack policy.
**Solution**: Ensure you are using an appropriate `AckPolicy`. If using durable consumers via the gateway, the consumer state is advanced automatically upon fetch, but only if the consumer is configured with `AckPolicy: none`. For reliable processing, your worker should fetch, process, and then acknowledge messages using a NATS client.

### Problem: Consumer not receiving new messages.

**Cause**: The consumer's deliver policy might be set to `last` and it is caught up, or there's a misconfiguration in the `FilterSubject`.
**Solution**: Check the consumer's configuration using `nats consumer info`. Ensure the `FilterSubject` matches the subjects you are publishing to.

### Problem: Timeout errors when fetching.

**Cause**: There are not enough messages in the stream to satisfy the `limit` parameter within the specified `timeout`. This is expected behavior.
**Solution**: Reduce the `limit`, increase the `timeout`, or design your client to handle partial results gracefully.

### Problem: HTTP calls return 404 for an existing stream.

**Cause**: Stream name casing mismatch between the CLI (often uppercase) and the HTTP path segment.
**Solution**: Call `GET /api/Streams/{stream}` using the exact casing shown in `nats stream ls`. Update gateways or CLI scripts to use consistent casing or set `STREAM_PREFIX` to normalize names.

### Problem: `POST /api/consumers/{stream}` fails with 404.

**Cause**: The stream does not exist yet; CLI would implicitly create it, but the gateway will not.
**Solution**: Create the stream first via NATS CLI (`nats stream add …`) or publish through the gateway, which auto-creates only when `AllowDirect` is enabled by your JetStream admin.

### Problem: Durable fetch never advances.

**Cause**: The consumer is configured with an explicit ack policy, but HTTP fetches mark progress only when the server auto-acks.
**Solution**: Either configure the consumer with `AckPolicy: none` for HTTP-only workflows or process/ack messages using a native NATS client. Mixing both requires acknowledging from the worker that performs business logic.

## FAQ

### What is a Consumer in NATS JetStream?
A consumer is a stateful view of a stream that acts as an interface for clients to access a subset of stored messages. The NATS server maintains the consumer's state, including its position (cursor) in the stream and which messages have been acknowledged.

### What is the difference between Push and Pull Consumers?
- **Push Consumers**: The server proactively "pushes" messages to a client as they become available. This is low-latency but can overwhelm a slow client.
- **Pull Consumers**: The client "pulls" messages from the server on demand. This gives the client control over the consumption rate and is the model used by this HTTP gateway.

### What are Durable vs. Ephemeral Consumers?
- **Durable Consumers**: Persist their state (last acknowledged message) on the server across client restarts. They are identified by a unique name and are ideal for production systems.
- **Ephemeral Consumers**: Are temporary and automatically deleted when the client disconnects or becomes inactive. They are useful for one-off tasks.

### How does NATS JetStream ensure message delivery?
JetStream provides at-least-once delivery guarantees by requiring clients to explicitly acknowledge (ack) messages after processing. If a message is not acknowledged within a specified `AckWait` time, the consumer will automatically re-deliver it.

### Can multiple applications use the same durable consumer?
Yes. When multiple clients bind to the same durable consumer name, they form a queue group. NATS automatically distributes messages among them for load balancing and horizontal scaling.

### Can a consumer filter messages from a stream?
Yes, consumers can be configured with a `FilterSubject` to receive only a subset of messages from the stream.