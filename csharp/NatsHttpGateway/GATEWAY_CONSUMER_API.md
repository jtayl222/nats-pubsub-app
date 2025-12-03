# NATS HTTP Gateway: Consumer API Reference

This document provides a detailed reference for the consumer-related API endpoints in the NATS HTTP Gateway.

## Table of Contents
- Quick Reference
- Pre-flight Checklist
- Fetching Messages
  - Fetch via Ephemeral Consumer
  - Fetch via Durable Consumer
- Consumer Management
  - Create a Consumer
  - List Consumers
  - Get Consumer Details
  - Delete a Consumer
  - Get Consumer Templates
- Consumer Operations
  - Peek at Consumer Messages
  - Reset a Consumer
- Consumer Monitoring
  - Check Consumer Health
  - Get Consumer Metrics History
- Protobuf Endpoints
  - Publish via Protobuf
  - Fetch via Protobuf
- WebSocket Streaming
  - Stream via Ephemeral Consumer
  - Stream via Durable Consumer
- Testing Cookbook

---

## Quick Reference

| Capability | Method & Path | Typical Status Codes | Notes |
|------------|---------------|----------------------|-------|
| Health | `GET /Health` | 200, 500 | Confirms NATS + JetStream connectivity before any other call |
| Stream info | `GET /api/Streams/{stream}` | 200, 404 | Verify the target stream exists; respects stream-name casing |
| Publish (JSON) | `POST /api/messages/{subject}` | 200, 500 | Auto-creates streams when possible; returns publish ack |
| Publish (Protobuf) | `POST /api/proto/protobufmessages/{subject}` | 200, 400, 500 | Accepts `PublishMessage` bytes; responds with `PublishAck` bytes |
| Ephemeral fetch | `GET /api/messages/{subjectFilter}` | 200, 400, 500 | Stateless; always returns last N messages |
| Durable fetch | `GET /api/messages/{stream}/consumer/{consumer}` | 200, 400, 404, 500 | Requires pre-created consumer |
| Consumer create | `POST /api/consumers/{stream}` | 201, 400, 404, 500 | Accepts `CreateConsumerRequest`; use templates for starters |
| Consumer delete | `DELETE /api/consumers/{stream}/{consumer}` | 200, 404, 500 | Cleans up durable consumers |
| Templates | `GET /api/consumers/templates` | 200 | Library of starter configs |
| Peek | `GET /api/consumers/{stream}/{consumer}/messages` | 200, 404, 500 | Reads without advancing cursor |
| Reset | `POST /api/consumers/{stream}/{consumer}/reset` | 200, 404, 500 | Replays from beginning/sequence/time |
| Health | `GET /api/consumers/{stream}/{consumer}/health` | 200, 404, 500 | Activity + lag snapshot |
| Metrics | `GET /api/consumers/{stream}/{consumer}/metrics/history` | 200, 404, 500 | Returns current metrics snapshot |

---

## Pre-flight Checklist

1. **Health** – `GET /Health` should return `{"status":"healthy","nats_connected":true,"jetstream_available":true}`. If not, fix the gateway or JetStream before proceeding.
2. **Stream availability** – `GET /api/Streams/{streamName}` ensures your stream exists (case-sensitive). Create it via NATS CLI or publish call if needed.
3. **Environment variables** – Ensure `NATS_URL`, `STREAM_PREFIX`, and any authentication settings are correct in `appsettings.*` or your container runtime.
4. **Optional** – For protobuf workflows, generate the client stubs from `Protos/message.proto` and install the `protobuf` runtime for your language.

---

## Fetching Messages

### Fetch via Ephemeral Consumer

Fetches the last N messages from a subject using a temporary, auto-deleted consumer. This is for stateless reads.

`GET /api/messages/{subjectFilter}`

**Use Case**: Quickly inspect the most recent messages on a subject without needing to manage consumer state.

**Path Parameters:**
- `subjectFilter` (string): NATS subject filter (e.g., `events.test`, `events.*`, `events.>`).

**Query Parameters:**
- `limit` (int): Number of messages to retrieve (1-100, default: 10).
- `timeout` (int): Timeout in seconds to wait for messages (1-30, default: 5).

**Example:**
```bash
curl "http://localhost:8080/api/messages/events.test?limit=5"
```

**Response (200 OK):**
```json
{
  "subject": "events.test",
  "count": 5,
  "stream": "events",
  "messages": [
    {
      "subject": "events.test",
      "sequence": 101,
      "timestamp": "2025-12-01T10:30:00Z",
      "data": { "message": "Hello" },
      "sizeBytes": 50
    }
  ]
}
```

### Fetch via Durable Consumer

Fetches the next batch of messages from a pre-existing, named consumer. This tracks read position.

`GET /api/messages/{stream}/consumer/{consumerName}`

**Use Case**: Process messages in a stream incrementally, where the gateway remembers your position between calls.

**Path Parameters:**
- `stream` (string): The name of the NATS stream.
- `consumerName` (string): The name of the durable consumer.

**Query Parameters:**
- `limit` (int): Number of messages to retrieve (1-100, default: 10).
- `timeout` (int): Timeout in seconds (1-30, default: 5).

**Example:**
```bash
# First call
curl "http://localhost:8080/api/messages/events/consumer/my-processor?limit=10"
# Returns messages 1-10

# Second call
curl "http://localhost:8080/api/messages/events/consumer/my-processor?limit=10"
# Returns messages 11-20
```

**Error Response (404 Not Found):**
- Returns `404` if the consumer does not exist. You must create it first.

---

## Consumer Management

### Create a Consumer

Creates a new durable or ephemeral consumer on a stream.

`POST /api/consumers/{stream}`

**Path Parameters:**
- `stream` (string): The stream to create the consumer on.

**Request Body:** (`CreateConsumerRequest`)
```json
{
  "name": "my-new-processor",
  "durable": true,
  "filterSubject": "events.orders.>",
  "deliverPolicy": "all",
  "ackPolicy": "explicit",
  "maxDeliver": 5,
  "ackWait": "00:00:30"
}
```

**Response (201 Created):**
- Returns the full details of the created consumer.

### List Consumers

Lists all consumers configured for a specific stream.

`GET /api/consumers/{stream}`

**Example:**
```bash
curl "http://localhost:8080/api/consumers/events"
```

### Get Consumer Details

Retrieves detailed configuration, state, and metrics for a single consumer.

`GET /api/consumers/{stream}/{consumer}`

**Example:**
```bash
curl "http://localhost:8080/api/consumers/events/my-processor"
```

### Delete a Consumer

Deletes a consumer from a stream.

`DELETE /api/consumers/{stream}/{consumer}`

**Example:**
```bash
curl -X DELETE "http://localhost:8080/api/consumers/events/my-processor"
```

### Get Consumer Templates

Returns a list of pre-defined consumer configurations for common use cases.

`GET /api/consumers/templates`

---

## Consumer Operations

### Peek at Consumer Messages

Retrieves the next messages from a consumer's perspective *without* advancing the consumer's state or acknowledging them.

`GET /api/consumers/{stream}/{consumer}/messages`

**Use Case**: Debugging what a consumer will receive next without impacting its normal operation.

**Query Parameters:**
- `limit` (int): Number of messages to peek (default: 10).

**Example:**
```bash
curl "http://localhost:8080/api/consumers/events/my-processor/messages?limit=3"
```

### Reset a Consumer

Resets a consumer's delivery state, allowing it to re-process messages.

`POST /api/consumers/{stream}/{consumer}/reset`

**Request Body:** (`ConsumerResetRequest`)
- **To replay all messages:**
  ```json
  { "action": "reset" }
  ```
- **To replay from a sequence number:**
  ```json
  { "action": "replay_from_sequence", "sequence": 12345 }
  ```
- **To replay from a specific time:**
  ```json
  { "action": "replay_from_time", "time": "2025-01-15T14:00:00Z" }
  ```

---

## Consumer Monitoring

### Check Consumer Health

Provides a health status check for a consumer, reporting on inactivity, pending messages, and acknowledgment lag.

`GET /api/consumers/{stream}/{consumer}/health`

**Example:**
```bash
curl "http://localhost:8080/api/consumers/events/my-processor/health"
```

### Get Consumer Metrics History

Retrieves a snapshot of the consumer's current metrics. (Note: This endpoint currently provides a single snapshot, not a time-series history).

`GET /api/consumers/{stream}/{consumer}/metrics/history`

---

## Protobuf Endpoints

Use these when clients exchange binary protobuf payloads defined in `Protos/message.proto`.

### Publish via Protobuf

`POST /api/proto/protobufmessages/{subject}`

- **Consumes**: `application/x-protobuf` (`nats.messages.PublishMessage`)
- **Produces**: `application/x-protobuf` (`nats.messages.PublishAck`)
- **Use Case**: Native protobuf clients publishing messages without JSON serialization.

**Example (curl):**
```bash
curl -X POST \
  "http://localhost:8080/api/proto/protobufmessages/events.demo" \
  -H "Content-Type: application/x-protobuf" \
  --data-binary @publish_message.bin \
  --output ack.bin
```

### Fetch via Protobuf

`GET /api/proto/protobufmessages/{subject}?limit=10`

- Returns `nats.messages.FetchResponse` bytes containing `FetchedMessage` entries.
- Parameters mirror the JSON endpoint.

**Example:**
```bash
curl -X GET \
  "http://localhost:8080/api/proto/protobufmessages/events.demo?limit=5" \
  -H "Accept: application/x-protobuf" \
  --output fetch.bin
```

---

## WebSocket Streaming

The gateway provides WebSocket endpoints to stream messages in real-time. Messages are sent as Protobuf-encoded binary frames.

### Stream via Ephemeral Consumer

Streams new messages from a subject using a temporary consumer.

`WS /ws/websocketmessages/{subjectFilter}`

**Use Case**: A real-time UI component that needs to display live events as they happen.

**Example (JavaScript client):**
```javascript
const ws = new WebSocket("ws://localhost:8080/ws/websocketmessages/events.>");

ws.onmessage = (event) => {
  // event.data will be a Protobuf binary frame
  console.log("Received message:", event.data);
};
```

### Stream via Durable Consumer

Streams messages from a durable consumer, tracking position across reconnects.

`WS /ws/websocketmessages/{stream}/consumer/{consumerName}`

**Use Case**: A long-running background service that needs to process all messages from a stream reliably, even if it disconnects and reconnects.

---

## Testing Cookbook

The repository ships with `tests/consumer_uat.py`, an interactive Python harness that exercises every REST endpoint with human checkpoints.

1. **Dependencies** – `python -m pip install -r Examples/requirements.txt protobuf requests rich` (or similar).
2. **Generate protobuf stubs** – `python -m grpc_tools.protoc -I=Protos --python_out=Examples Protos/message.proto` (one-time).
3. **Environment variables** – override defaults with:
  - `GATEWAY_BASE_URL` (default `http://localhost:8080`)
  - `GATEWAY_STREAM`, `GATEWAY_SUBJECT`, `GATEWAY_CONSUMER`
  - `GATEWAY_AUTO_ADVANCE=true` to skip pauses during CI runs.
4. **Run** – `python tests/consumer_uat.py`. The script will:
  - Call `/Health` and `/api/Streams/{stream}` before touching data.
  - Create/list/get/delete consumers.
  - Publish JSON + protobuf payloads, fetch via durable consumer, peek, reset, and verify deletion.
  - Save a JSON transcript (`consumer_uat_log.json`) containing each step’s response and expectation text.

**Recommended practice**: Capture the log artifact for audit purposes and rerun with a fresh stream/consumer name each time to keep tests idempotent.