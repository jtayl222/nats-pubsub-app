# NATS HTTP Gateway: Consumer API Reference

This document provides a detailed reference for the consumer-related API endpoints in the NATS HTTP Gateway.

## Table of Contents
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
- WebSocket Streaming
  - Stream via Ephemeral Consumer
  - Stream via Durable Consumer

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