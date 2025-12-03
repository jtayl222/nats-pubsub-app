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