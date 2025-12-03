# NATS JetStream Consumers Guide

This guide explains how to retrieve messages from NATS, the different paradigms for data consumption, terminology mappings to other messaging systems, and the two different methods for fetching messages from NATS JetStream in the NatsHttpGateway.

## Table of Contents
- [Introduction: Getting Data from NATS](#introduction-getting-data-from-nats)
- [Core NATS vs JetStream](#core-nats-vs-jetstream)
- [Terminology: Consumers vs Subscribers](#terminology-consumers-vs-subscribers)
- [Mapping to Other Messaging Systems](#mapping-to-other-messaging-systems)
- [Overview](#overview)
- [GET Endpoint Comparison](#get-endpoint-comparison)
- [Consumer Lifecycles](#consumer-lifecycles)
- [Ephemeral Consumers](#ephemeral-consumers)
- [Durable Consumers](#durable-consumers)
- [Creating a Durable Consumer](#creating-a-durable-consumer)
- [Consumer Templates](#consumer-templates)
- [Use Cases](#use-cases)
- [Hands-on UAT Script](#hands-on-uat-script)
- [API Examples](#api-examples)

## Introduction: Getting Data from NATS

NATS provides two distinct paradigms for consuming messages:

1. **Core NATS (Basic Pub/Sub)** - Real-time, fire-and-forget messaging with no persistence
2. **JetStream** - Stream-based messaging with persistence, replay, and delivery guarantees

### When to Use Each Paradigm

**Use Core NATS when:**
- You need ultra-low latency (sub-millisecond)
- Messages are ephemeral and don't need to be stored
- You're building real-time systems (chat, live updates, telemetry)
- Subscribers are always online
- Message loss is acceptable

**Use JetStream when:**
- You need message persistence and durability
- You want to replay historical messages
- Consumers may be offline and need to catch up
- You need delivery guarantees (at-least-once, exactly-once)
- You're building event sourcing or audit trails

**This gateway focuses on JetStream** because it provides HTTP access to persisted message streams, making it ideal for web applications and microservices that need reliable message delivery.

## Core NATS vs JetStream

### Core NATS (Subscribers)

In Core NATS, clients create **subscribers** that receive messages in real-time as they're published:

```
Publisher → NATS Server → Subscriber (active connection)
                      ↓
                   (message lost if no subscriber)
```

**Characteristics:**
- **No persistence** - Messages exist only in memory
- **Push-based** - Server pushes messages to active subscribers immediately
- **Fire-and-forget** - If no subscriber is listening, message is lost
- **No replay** - Can't retrieve historical messages
- **Stateless** - Server doesn't track delivery or acknowledgement

**Core NATS is NOT available through this HTTP gateway** because HTTP is request/response based and can't maintain the persistent connections required for Core NATS subscriptions. For Core NATS, use native NATS client libraries with long-lived connections.

### JetStream (Consumers)

JetStream adds **streams** (message storage) and **consumers** (stateful message readers):

```
Publisher → NATS Stream (persistent) → Consumer (pulls messages)
                    ↓
            (messages stored on disk)
```

**Characteristics:**
- **Persistent storage** - Messages stored on disk in streams
- **Pull or push** - Consumers can pull messages or receive pushes
- **Replay capable** - Can retrieve historical messages
- **Stateful** - Tracks which messages have been delivered/acknowledged
- **Delivery guarantees** - At-least-once or exactly-once semantics

**JetStream is ideal for HTTP gateways** because it allows stateless HTTP requests to retrieve messages at any time, with full control over message delivery and acknowledgement.

## Terminology: Consumers vs Subscribers

NATS uses different terminology for its two paradigms:

### Subscriber (Core NATS)
A **subscriber** is a client connection that receives real-time messages on a subject:
- Lightweight, stateless
- Messages delivered immediately via push
- No acknowledgement tracking
- Connection-based (must be actively connected)
- Example: `nats.Subscribe("events.*")`

### Consumer (JetStream)
A **consumer** is a durable, server-side entity that tracks message delivery:
- Heavyweight, stateful
- Messages pulled on-demand or pushed
- Tracks acknowledgements and redelivery
- Can be ephemeral (temporary) or durable (persistent)
- Example: Create consumer, then fetch messages from it

**Key difference:** A subscriber is just a client receiving messages. A consumer is a server-side object with configuration, state, and delivery tracking.

## Mapping to Other Messaging Systems

If you're familiar with Kafka or RabbitMQ, here's how NATS concepts map:

### Kafka → NATS Mapping

| Kafka Concept | NATS Equivalent | Notes |
|---------------|-----------------|-------|
| **Topic** | **Subject** (Core) or **Stream** (JetStream) | Stream is closer to Kafka topic with partitions |
| **Partition** | **Stream** (no built-in partitioning) | NATS doesn't partition; use multiple streams/subjects |
| **Consumer Group** | **Durable Consumer** (JetStream) | Both track offset/position |
| **Consumer** | **Subscriber** (Core) or **Consumer** (JetStream) | JetStream consumer ≈ Kafka consumer |
| **Offset** | **Sequence Number** | Both track message position in stream |
| **Commit** | **Ack** (Acknowledgement) | Both confirm message processing |
| **Compaction** | Not directly supported | Use subject-based filtering instead |
| **Replication** | **Clustering/Replication** | Both support HA with replicas |

**Key differences from Kafka:**
- NATS subjects use wildcards (`events.*`, `events.>`) instead of topic prefixes
- No built-in partitioning - scale horizontally with multiple streams/subjects
- Lighter weight, lower latency
- Simpler operational model (no Zookeeper/KRaft)

### RabbitMQ → NATS Mapping

| RabbitMQ Concept | NATS Equivalent | Notes |
|------------------|-----------------|-------|
| **Exchange** | **Subject hierarchy** | NATS uses subject patterns instead of exchanges |
| **Queue** | **Consumer** (JetStream) | Both buffer messages for workers |
| **Binding** | **FilterSubject** (consumer config) | Maps subjects to consumers |
| **Consumer** | **Subscriber** (Core) or **Consumer** (JetStream) | Similar push/pull models |
| **Acknowledgement** | **Ack** | Both confirm delivery |
| **Dead Letter Queue** | Not built-in | Implement with max delivery + subject routing |
| **TTL** | **MaxAge** (stream config) | Both expire old messages |
| **Priority Queue** | Not supported | All messages equal priority |

**Key differences from RabbitMQ:**
- No exchange types (direct/topic/fanout) - all routing via subject patterns
- Simpler model: publish to subjects, subscribe to subjects
- No complex routing rules - use subject wildcards instead
- Better performance for high-throughput scenarios

### Summary Table

| Feature | Core NATS | JetStream | Kafka | RabbitMQ |
|---------|-----------|-----------|-------|----------|
| **Persistence** | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes |
| **Replay** | ❌ No | ✅ Yes | ✅ Yes | ❌ No |
| **Delivery Guarantees** | At-most-once | At-least-once, exactly-once | At-least-once | At-least-once |
| **Message Ordering** | Per-publisher | Per-stream | Per-partition | Per-queue |
| **Consumer Groups** | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes |
| **Latency** | Sub-ms | Low ms | Low ms | Low ms |
| **Throughput** | Very High | High | Very High | Medium-High |
| **Operational Complexity** | Very Low | Low | High | Medium |

## Overview

NATS JetStream provides two types of consumers for reading messages from streams:

1. **Ephemeral Consumers** - Temporary, auto-deleted after use
2. **Durable Consumers** - Persistent, maintain state across requests

The NatsHttpGateway provides two GET endpoints corresponding to these consumer types:

| Endpoint | Consumer Type | Use Case |
|----------|---------------|----------|
| `GET /api/Messages/{subjectFilter}` | Ephemeral | Stateless reads, last N messages |
| `GET /api/Messages/{stream}/consumer/{consumerName}` | Durable | Stateful reads, track position |

## GET Endpoint Comparison

### Ephemeral Consumer Endpoint

```http
GET /api/Messages/{subjectFilter}?limit=10&timeout=5
```

**How it works:**
1. Creates a temporary consumer on-the-fly
2. Fetches the last N messages from the stream
3. Automatically deletes the consumer after use
4. Does NOT track which messages you've read

**Parameters:**
- `subjectFilter` (path) - NATS subject filter (supports wildcards: `events.test`, `events.>`, `events.*`)
- `limit` (query) - Number of messages to retrieve (1-100, default: 10)
- `timeout` (query) - Timeout in seconds (1-30, default: 5)

**Example:**
```bash
curl "http://localhost:8080/api/Messages/events.test?limit=20&timeout=10"
```

**Response:**
```json
{
  "subject": "events.test",
  "count": 20,
  "stream": "events",
  "messages": [
    {
      "subject": "events.test",
      "sequence": 1,
      "timestamp": "2025-12-01T10:30:00Z",
      "data": { "message": "Hello" },
      "sizeBytes": 50
    }
  ]
}
```

### Durable Consumer Endpoint

```http
GET /api/Messages/{stream}/consumer/{consumerName}?limit=10&timeout=5
```

**How it works:**
1. Uses an existing, pre-created durable consumer
2. Fetches the next N messages from where the consumer left off
3. Consumer state persists between requests
4. DOES track which messages you've read (based on consumer configuration)

**Parameters:**
- `stream` (path) - NATS stream name (e.g., `events`, `payments`)
- `consumerName` (path) - Name of the durable consumer (e.g., `my-service-consumer`)
- `limit` (query) - Number of messages to retrieve (1-100, default: 10)
- `timeout` (query) - Timeout in seconds (1-30, default: 5)

**Example:**
```bash
curl "http://localhost:8080/api/Messages/events/consumer/my-service-consumer?limit=20&timeout=10"
```

**Response:** (same format as ephemeral endpoint)

**Error Handling:**
- Returns `404 Not Found` if the consumer doesn't exist
- Error message includes instructions to create the consumer first

## Consumer Lifecycles

Visualize the two primary flows supported by the gateway to decide which runtime fits your workload.

```
Ephemeral Fetch (stateless)
┌────────────┐   GET /api/messages/{subject}   ┌────────────┐
│ HTTP Call  │────────────────────────────────▶│ Gateway    │
└────────────┘                                  │ ┌───────┐ │
  ▲                                        │ │Create │ │
  │ response                               │ │Temp   │ │
  │                                        │ │Consumer│ │
  │                                        │ └─┬───┬─┘ │
  │                                        │   │   │   │
  │                                        │ Fetch  │  │
  │                                        │ Delete │  │
  ▼                                        └────────────┘
```

Steps: request arrives, gateway calculates a start sequence, creates a short-lived consumer, fetches the last *N* messages, returns them, and deletes the consumer immediately.

```
Durable Workflow (stateful)
┌────────────┐   POST /api/consumers/{stream}   ┌────────────┐
│ Provision  │────────────────────────────────▶│ Gateway    │
└────────────┘                                  └────┬───────┘
      (one-time)                     create durable

┌────────────┐   GET /api/messages/{stream}/consumer/{name}
│ Worker     │──────────────────────────────────────────────▶ maintains cursor
└────────────┘   GET /api/consumers/{stream}/{name}/health   │
                    │ monitor/reset/delete
```

Steps: provision (CLI/API/template or POST) the consumer, fetch repeatedly via the durable endpoint which advances the server-side cursor, monitor via `/health`, `/metrics/history`, peek/reset as needed, and delete once decommissioned.

## Ephemeral Consumers

### Characteristics

- **Lifespan**: Created on-demand, deleted immediately after use
- **State**: No state persistence - each request is independent
- **Position**: Always fetches the last N messages (by sequence number)
- **Name**: Auto-generated unique name (e.g., `http-fetch-abc123`)
- **Configuration**: Hardcoded in NatsService.cs:
  - `DeliverPolicy`: ByStartSequence (calculated to get last N)
  - `AckPolicy`: None (no acknowledgment needed)
  - `InactiveThreshold`: 5 seconds

### Internal Implementation

From `NatsService.cs` (line 93-183):

```csharp
public async Task<FetchMessagesResponse> FetchMessagesAsync(string subject, int limit = 10, int timeoutSeconds = 5)
{
    // Calculate start sequence to get last N messages
    var startSeq = (ulong)Math.Max(1, (long)lastSeq - limit + 1);

    // Create ephemeral consumer
    var consumerConfig = new ConsumerConfig
    {
        Name = $"http-fetch-{Guid.NewGuid()}",
        DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
        OptStartSeq = startSeq,
        FilterSubject = subject,
        AckPolicy = ConsumerConfigAckPolicy.None,
        InactiveThreshold = TimeSpan.FromSeconds(5)
    };

    var consumer = await _js.CreateConsumerAsync(streamName, consumerConfig);

    // Fetch messages
    await foreach (var msg in consumer.FetchAsync<byte[]>(...))
    {
        // Process messages
    }

    // Cleanup: Delete the ephemeral consumer
    await _js.DeleteConsumerAsync(streamName, consumerConfig.Name);
}
```

### Advantages

✅ **Simplicity** - No setup required, just call the endpoint
✅ **Stateless** - No state to manage or clean up
✅ **Idempotent** - Same request always returns same results (if stream unchanged)
✅ **No resource leaks** - Auto-cleanup prevents consumer buildup

### Disadvantages

❌ **No progress tracking** - Can't continue where you left off
❌ **Always last N** - Can't start from beginning, specific sequence, or time
❌ **Performance overhead** - Creates/deletes consumer on every request
❌ **Not suitable for processing workflows** - No acknowledgment or redelivery

## Durable Consumers

### Characteristics

- **Lifespan**: Persists indefinitely until explicitly deleted
- **State**: Maintains position (last delivered sequence)
- **Position**: Configurable - can start from beginning, end, specific sequence, or time
- **Name**: User-defined, stable name (e.g., `payment-processor`)
- **Configuration**: Fully customizable via NATS CLI or API

### Internal Implementation

From `NatsService.cs` (line 185-260):

```csharp
public async Task<FetchMessagesResponse> FetchMessagesFromConsumerAsync(
    string subject, string consumerName, int limit = 10, int timeoutSeconds = 5)
{
    // Get the existing durable consumer
    INatsJSConsumer consumer;
    try
    {
        consumer = await _js.GetConsumerAsync(streamName, consumerName);
    }
    catch (NatsJSApiException ex) when (ex.Error.Code == 404)
    {
        throw new InvalidOperationException(
            $"Consumer '{consumerName}' does not exist in stream '{streamName}'. " +
            $"Please create the consumer first using the NATS CLI or management API.");
    }

    // Fetch messages from current position
    await foreach (var msg in consumer.FetchAsync<byte[]>(...))
    {
        // Process messages
    }

    // Consumer persists - NOT deleted
}
```

### Advantages

✅ **Progress tracking** - Remembers where you left off
✅ **Flexible starting position** - Begin from anywhere in the stream
✅ **Acknowledgment support** - Can configure ack requirements
✅ **Performance** - No consumer creation overhead per request
✅ **Message guarantees** - Redelivery on failure (if configured)

### Disadvantages

❌ **Requires setup** - Must create consumer before use
❌ **State management** - Need to understand consumer state
❌ **Resource usage** - Consumers consume server resources
❌ **Complexity** - More configuration options to understand

## Creating a Durable Consumer

### Prerequisites

Before using the durable consumer endpoint, you must create the consumer using one of these methods:

### Method 1: NATS CLI (Recommended)

Install the NATS CLI:
```bash
# macOS
brew install nats-io/nats-tools/nats

# Linux/Windows: Download from https://github.com/nats-io/natscli/releases
```

Create a consumer:
```bash
# Basic consumer starting from the beginning
nats consumer add events my-service-consumer \
  --filter events.test \
  --deliver all \
  --ack none \
  --replay instant

# Consumer with specific configuration
nats consumer add events payment-processor \
  --filter events.payment.> \
  --deliver last \
  --ack explicit \
  --max-deliver 3 \
  --replay instant \
  --max-ack-pending 100
```

**Common Options:**
- `--filter` - Subject filter (e.g., `events.test`, `events.>`)
- `--deliver` - Starting position:
  - `all` - From beginning of stream
  - `last` - From end of stream (only new messages)
  - `new` - Only messages published after consumer creation
  - `1h` - Messages from last hour
- `--ack` - Acknowledgment policy:
  - `none` - No acknowledgment required
  - `explicit` - Must explicitly ack each message
  - `all` - Ack acknowledges all previous messages
- `--max-deliver` - Max redelivery attempts (with `--ack explicit`)
- `--replay` - Delivery speed:
  - `instant` - As fast as possible
  - `original` - Replay at original timing

Verify the consumer was created:
```bash
nats consumer ls events
nats consumer info events my-service-consumer
```

### Method 2: NATS Management API

Use the NATS HTTP API (requires NATS server with monitoring enabled):

```bash
curl -X POST http://localhost:8222/jsz/api/v1/stream/events/consumer \
  -H "Content-Type: application/json" \
  -d '{
    "stream_name": "events",
    "config": {
      "durable_name": "my-service-consumer",
      "deliver_policy": "all",
      "ack_policy": "none",
      "filter_subject": "events.test",
      "replay_policy": "instant"
    }
  }'
```

### Method 3: Programmatically (C# with NATS.Client)

```csharp
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

var nats = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222" });
await nats.ConnectAsync();
var js = new NatsJSContext(nats);

var consumerConfig = new ConsumerConfig("my-service-consumer")
{
    DeliverPolicy = ConsumerConfigDeliverPolicy.All,
    AckPolicy = ConsumerConfigAckPolicy.None,
    FilterSubject = "events.test",
    ReplayPolicy = ConsumerConfigReplayPolicy.Instant
};

await js.CreateConsumerAsync("events", consumerConfig);
Console.WriteLine("Consumer created successfully!");
```

### Method 4: Using the NatsHttpGateway (Built-in)

The gateway exposes a first-class `POST /api/consumers/{stream}` endpoint that accepts the `CreateConsumerRequest` contract shown in `Models/ConsumerModels.cs`.

```bash
curl -X POST "http://localhost:8080/api/consumers/events" \
  -H "Content-Type: application/json" \
  -d '{
        "name": "my-service-consumer",
        "description": "Processes order events",
        "durable": true,
        "filterSubject": "events.orders.*",
        "deliverPolicy": "all",
        "ackPolicy": "explicit",
        "maxDeliver": 5,
        "ackWait": "00:01:00"
      }'
```

Pair this endpoint with [`GET /api/consumers/templates`](#consumer-templates) to bootstrap known-good configurations, then tweak per stream.

## Consumer Templates

Skip guesswork by starting from the curated templates baked into `NatsService`. Fetch them with:

```bash
curl http://localhost:8080/api/consumers/templates | jq .
```

Sample response excerpt:

```json
{
  "count": 6,
  "templates": [
    {
      "name": "batch-processor",
      "description": "Processes all messages from the beginning (durable)",
      "useCase": "Batch processing, data migration",
      "template": {
        "durable": true,
        "deliverPolicy": "all",
        "ackPolicy": "explicit",
        "maxDeliver": 5,
        "ackWait": "00:05:00"
      }
    }
  ]
}
```

To create a consumer from a template:

1. `GET /api/consumers/templates` and copy the `template` payload that best matches your scenario (real-time processor, work queue, fire-and-forget, latest-only, durable-processor, etc.).
2. Add stream-specific fields such as `filterSubject`, `name`, or `inactiveThreshold` if omitted.
3. `POST /api/consumers/{stream}` with the merged body.

Because templates are simple JSON, you can also check them into your infrastructure repo to keep runtime configuration auditable.

## Use Cases

### When to Use Ephemeral Consumers

✅ **Quick lookups** - "Show me the last 10 events"
✅ **Debugging** - Inspect recent messages
✅ **Read-only queries** - No need to track position
✅ **Stateless services** - Each request is independent
✅ **Dashboard displays** - Real-time view of recent activity
✅ **Testing** - Simple, no cleanup required

**Example Scenarios:**
- Admin dashboard showing recent payments
- Monitoring tool displaying last error events
- API endpoint for "show recent activity"
- Development/debugging during testing

### When to Use Durable Consumers

✅ **Message processing workflows** - Process each message exactly once
✅ **Event-driven systems** - React to events in order
✅ **Background jobs** - Consume and process over time
✅ **Stateful services** - Need to remember position
✅ **Guaranteed delivery** - With ack policy and redelivery
✅ **Long-running tasks** - Process messages over hours/days

## Hands-on UAT Script

The repository ships with `tests/consumer_uat.py`, a guided Python harness that walks through health checks, stream verification, consumer CRUD, JSON + protobuf publishing, fetch/peek/reset, and cleanup.

1. **Install dependencies** (once per machine):
  ```bash
  python -m pip install -r Examples/requirements.txt protobuf requests rich
  ```
2. **Generate protobuf stubs** if you plan to test binary routes:
  ```bash
  python -m grpc_tools.protoc -I=Protos --python_out=Examples Protos/message.proto
  ```
3. **Override defaults** via env vars as needed:
  - `GATEWAY_BASE_URL` (default `http://localhost:8080`)
  - `GATEWAY_STREAM`, `GATEWAY_SUBJECT`, `GATEWAY_CONSUMER`
  - `GATEWAY_AUTO_ADVANCE=true` to skip interactive pauses (great for CI)
4. **Run the script**:
  ```bash
  python tests/consumer_uat.py
  ```
5. **Review artifacts** – Each run writes `tests/consumer_uat_log.json`, capturing every request/response and the human expectation text for audit trails.

Use this harness whenever you touch consumer-related code or infrastructure updates; it exercises the same endpoints described in this guide and catches regression risks early.

**Example Scenarios:**
- Payment processor consuming transaction events
- Email service sending notifications for user events
- Analytics pipeline processing all events sequentially
- Audit log processor ensuring no events are missed
- Webhook dispatcher tracking delivery position

## API Examples

### Example 1: Ephemeral Consumer - Recent Activity Dashboard

```bash
# Fetch last 20 events for dashboard
curl "http://localhost:8080/api/Messages/events.user?limit=20&timeout=5"
```

**Response:**
```json
{
  "subject": "events.user",
  "count": 20,
  "stream": "events",
  "messages": [...]
}
```

**Characteristics:**
- Same request always returns last 20 messages
- No state - each request is independent
- Consumer created and deleted automatically

### Example 2: Durable Consumer - Event Processor

**Step 1: Create durable consumer (one-time setup)**
```bash
nats consumer add events user-event-processor \
  --filter events.user.> \
  --deliver all \
  --ack explicit \
  --max-deliver 3
```

**Step 2: Process messages in batches**
```bash
# First batch
curl "http://localhost:8080/api/Messages/events/consumer/user-event-processor?limit=10"
# Returns messages 1-10, consumer position advances to 10

# Second batch (continues where left off)
curl "http://localhost:8080/api/Messages/events/consumer/user-event-processor?limit=10"
# Returns messages 11-20, consumer position advances to 20

# Third batch
curl "http://localhost:8080/api/Messages/events/consumer/user-event-processor?limit=10"
# Returns messages 21-30, consumer position advances to 30
```

**Characteristics:**
- Each request continues where previous request left off
- Consumer tracks position persistently
- Can process entire stream incrementally

### Example 3: Timeout Behavior

**Ephemeral consumer with short timeout:**
```bash
# If stream has fewer than 50 messages, will timeout after 2 seconds
curl "http://localhost:8080/api/Messages/events.test?limit=50&timeout=2"
```

**Response** (if only 5 messages exist):
```json
{
  "subject": "events.test",
  "count": 5,
  "stream": "events",
  "messages": [...]
}
```

**Durable consumer with long timeout:**
```bash
# Wait up to 30 seconds for new messages
curl "http://localhost:8080/api/Messages/events/consumer/my-processor?limit=10&timeout=30"
```

**Behavior:**
- If 10 messages available: Returns immediately
- If only 3 messages available: Returns after timeout with 3 messages
- If no messages: Returns after timeout with empty array

## Timeout Behavior Details

Both endpoints support configurable timeouts (1-30 seconds):

### How Timeout Works

1. **Request starts** - Begin fetching from stream
2. **Messages arrive** - Collected until limit reached or timeout expires
3. **Timeout expires** - Return whatever messages were collected
4. **Limit reached** - Return immediately (before timeout)

### Timeout Use Cases

**Short timeout (1-5 seconds):**
- Quick responses for user-facing APIs
- Dashboard updates
- Health checks

**Medium timeout (5-15 seconds):**
- Background processing
- Batch operations
- Scheduled jobs

**Long timeout (15-30 seconds):**
- Waiting for new messages
- Long-polling scenarios
- Message queue workers

## Consumer Configuration Options

When creating durable consumers, you have full control over behavior:

### Deliver Policy

Controls where the consumer starts reading:

| Policy | Description | Use Case |
|--------|-------------|----------|
| `all` | Start from beginning | Process entire history |
| `last` | Start from last message | Only new events |
| `new` | Only messages after creation | Real-time only |
| `by_start_sequence` | Start from specific sequence | Resume from known point |
| `by_start_time` | Start from timestamp | Process from specific time |

### Ack Policy

Controls message acknowledgment requirements:

| Policy | Description | Use Case |
|--------|-------------|----------|
| `none` | No acknowledgment | Read-only, no guarantees |
| `explicit` | Each message must be acked | Guaranteed processing |
| `all` | Ack acknowledges all prior | Batch processing |

### Replay Policy

Controls delivery speed:

| Policy | Description | Use Case |
|--------|-------------|----------|
| `instant` | As fast as possible | Normal operation |
| `original` | Replay at original timing | Testing, simulation |

## Monitoring and Management

### Check Consumer Status

```bash
# List all consumers for a stream
nats consumer ls events

# Get detailed consumer info
nats consumer info events my-service-consumer
```

**Output:**
```
Information for Consumer events > my-service-consumer

Configuration:
    Durable Name: my-service-consumer
    Filter Subject: events.test
    Deliver Policy: all
    Ack Policy: none
    Replay Policy: instant

State:
    Last Delivered Sequence: 42
    Num Ack Pending: 0
    Num Redelivered: 0
    Num Waiting: 0
    Num Pending: 158
```

**Key metrics:**
- `Last Delivered Sequence` - Position in stream
- `Num Pending` - Messages not yet delivered
- `Num Ack Pending` - Messages awaiting acknowledgment
- `Num Redelivered` - Failed delivery retries

### Delete Consumer

```bash
nats consumer rm events my-service-consumer
```

## Error Handling

### Consumer Not Found (404)

When calling the durable consumer endpoint with a non-existent consumer:

```bash
curl "http://localhost:8080/api/Messages/events/consumer/does-not-exist"
```

**Response:**
```json
{
  "error": "Consumer 'does-not-exist' does not exist in stream 'events'. Please create the consumer first using the NATS CLI or management API."
}
```

**HTTP Status:** 404 Not Found

**Solution:** Create the consumer using NATS CLI or API (see [Creating a Durable Consumer](#creating-a-durable-consumer))

### Stream Not Found

If the stream doesn't exist for the subject:

```bash
curl "http://localhost:8080/api/Messages/nonexistent.subject"
```

**Response:**
```json
{
  "title": "Fetch failed",
  "detail": "Stream not found for subject",
  "status": 500
}
```

**Solution:** Publish a message to create the stream automatically, or create it explicitly via NATS CLI

### Invalid Parameters

```bash
curl "http://localhost:8080/api/Messages/events.test?limit=500&timeout=100"
```

**Response:**
```json
{
  "error": "Limit must be between 1 and 100"
}
```

**HTTP Status:** 400 Bad Request

## Best Practices

### For Ephemeral Consumers

1. **Use for read-only operations** - Inspecting, monitoring, debugging
2. **Keep limits reasonable** - Don't fetch thousands of messages
3. **Set appropriate timeouts** - 5s for interactive, 10-30s for batch
4. **Cache results if needed** - Avoid repeated identical requests

### For Durable Consumers

1. **Name consumers clearly** - Use descriptive names (e.g., `payment-processor`, not `consumer1`)
2. **Configure ack policy appropriately** - Use `explicit` for critical processing
3. **Set max-deliver for retries** - Prevent infinite redelivery loops
4. **Monitor consumer lag** - Check `Num Pending` to detect backlog
5. **Clean up unused consumers** - Delete consumers no longer needed
6. **One consumer per processing instance** - Don't share consumers across services
7. **Use filter subjects** - Narrow scope to needed messages

## Troubleshooting

### Problem: Consumer keeps re-reading same messages

**Cause:** Ack policy is `none` or messages aren't being acknowledged

**Solution:**
- For durable consumers, use `--ack explicit` when creating
- Implement proper acknowledgment in your processing code
- Check consumer info to see `Last Delivered Sequence`

### Problem: Messages being delivered multiple times

**Cause:** Acknowledgment timeout too short, or processing failures

**Solution:**
- Increase `--max-ack-pending` timeout
- Implement idempotent message processing
- Set appropriate `--max-deliver` limit

### Problem: Consumer not receiving new messages

**Cause:** Consumer position is at end of stream

**Solution:**
- Check consumer info: `nats consumer info events my-consumer`
- Reset consumer position: Delete and recreate
- Or create new consumer with `--deliver last`

### Problem: Timeout errors

**Cause:** Not enough messages in stream to satisfy limit within timeout

**Solution:**
- Reduce `limit` parameter
- Increase `timeout` parameter
- Handle partial results gracefully in your code

## Summary

| Feature | Ephemeral Consumer | Durable Consumer |
|---------|-------------------|------------------|
| **Setup Required** | None | Must create first |
| **State Persistence** | No | Yes |
| **Starting Position** | Last N messages | Configurable |
| **Progress Tracking** | No | Yes |
| **Acknowledgment** | Not supported | Configurable |
| **Redelivery** | No | Configurable |
| **Performance** | Create/delete overhead | Fast |
| **Use Case** | Quick reads | Message processing |
| **API Endpoint** | `GET /api/Messages/{subjectFilter}` | `GET /api/Messages/{stream}/consumer/{name}` |

## Additional Resources

- [NATS JetStream Documentation](https://docs.nats.io/nats-concepts/jetstream)
- [NATS CLI Guide](https://github.com/nats-io/natscli)
- [NATS .NET Client](https://github.com/nats-io/nats.net.v2)
- [NatsHttpGateway Protobuf Guide](./PROTOBUF_GUIDE.md)
- [NatsHttpGateway Troubleshooting](./PROTOBUF_TROUBLESHOOTING.md)
