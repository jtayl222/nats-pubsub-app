# NATS HTTP Gateway

HTTP/REST gateway for NATS JetStream messaging. Provides stateless HTTP endpoints for publishing and consuming messages from NATS subjects.

## Features

- ✅ **RESTful API** - Standard HTTP/JSON interface
- ✅ **WebSocket Streaming** - Real-time message subscription with protobuf frames
- ✅ **JetStream Integration** - Persistent message storage and replay
- ✅ **Auto-Stream Creation** - Streams are created automatically
- ✅ **Stateless Design** - Scales horizontally without session management
- ✅ **Dynamic Subjects** - Publish/fetch from any subject via URL
- ✅ **Durable Consumers** - Support for both ephemeral and durable consumers
- ✅ **Swagger/OpenAPI** - Interactive API documentation
- ✅ **Health Checks** - Monitor NATS connectivity
- ✅ **JWT Authentication** - Optional Bearer token validation for REST API
- ✅ **mTLS Support** - Optional mutual TLS for NATS connections

## Architecture

```
HTTP Client → NatsHttpGateway → NATS JetStream → Streams
                  ↑                                   ↓
                  └────────────── Fetch ──────────────┘
```

**Key Design Decisions:**
- **Singleton NATS connection** - Reused across all requests for efficiency
- **Ephemeral consumers** - Created per fetch request, auto-deleted after
- **No persistent subscriptions** - Stateless for scalability
- **Stream auto-creation** - Based on subject naming convention

## API Endpoints

### Health Check
```bash
GET /health
```

**Response:**
```json
{
  "status": "healthy",
  "nats_connected": true,
  "nats_url": "nats://localhost:4222",
  "jetstream_available": true,
  "timestamp": "2025-11-24T22:00:00.000Z"
}
```

---

### Publish Message
```bash
POST /api/messages/{subject}
Content-Type: application/json

{
  "message_id": "optional-custom-id",
  "source": "my-app",
  "data": {
    "event_type": "user.login",
    "user_id": 12345,
    "custom_field": "any JSON data"
  }
}
```

**Response:**
```json
{
  "published": true,
  "subject": "events.test",
  "stream": "EVENTS",
  "sequence": 42,
  "timestamp": "2025-11-24T22:00:00.000Z"
}
```

**Examples:**
```bash
# Publish to events.test
curl -X POST http://localhost:8080/api/messages/events.test \
  -H "Content-Type: application/json" \
  -d '{"data": {"event_type": "test", "value": 123}}'

# Publish to payments.approved
curl -X POST http://localhost:8080/api/messages/payments.approved \
  -H "Content-Type: application/json" \
  -d '{"data": {"amount": 99.99, "currency": "USD"}}'
```

---

### Fetch Messages
```bash
GET /api/messages/{subject}?limit=10
```

**Query Parameters:**
- `limit` (optional) - Number of messages to fetch (1-100, default: 10)

**Response:**
```json
{
  "subject": "events.test",
  "count": 10,
  "stream": "EVENTS",
  "messages": [
    {
      "subject": "events.test",
      "sequence": 42,
      "timestamp": "2025-11-24T22:00:00.000Z",
      "data": {
        "message_id": "msg-123",
        "timestamp": "2025-11-24T22:00:00.000Z",
        "source": "my-app",
        "data": {"event_type": "user.login"}
      },
      "size_bytes": 156
    }
  ]
}
```

**Examples:**
```bash
# Fetch last 10 messages
curl http://localhost:8080/api/messages/events.test?limit=10

# Fetch last 50 messages
curl http://localhost:8080/api/messages/payments.approved?limit=50
```

---

### WebSocket Streaming (Real-time)

Stream messages in real-time using WebSocket connections with protobuf binary frames.

#### Stream from Ephemeral Consumer
```
WS /ws/websocketmessages/{subjectFilter}
```

Connects to an ephemeral consumer that streams new messages matching the subject filter.

**Example (using wscat):**
```bash
# Install wscat if needed
npm install -g wscat

# Stream messages from events.> subject filter
wscat -c "ws://localhost:8080/ws/websocketmessages/events.>"

# Stream messages from specific subject
wscat -c "ws://localhost:8080/ws/websocketmessages/events.test"
```

**Frame Format:**
All messages are sent as protobuf binary frames (`WebSocketFrame` message type):

```protobuf
message WebSocketFrame {
  FrameType type = 1;  // MESSAGE or CONTROL
  oneof payload {
    StreamMessage message = 2;  // NATS message data
    ControlMessage control = 3;  // Control/status messages
  }
}
```

**Example Client (C#):**
```csharp
var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("ws://localhost:8080/ws/websocketmessages/events.>"), cts.Token);

while (ws.State == WebSocketState.Open) {
    var buffer = new byte[16384];
    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

    var frameBytes = new byte[result.Count];
    Array.Copy(buffer, frameBytes, result.Count);

    var frame = WebSocketFrame.Parser.ParseFrom(frameBytes);
    // Handle frame.Message or frame.Control
}
```

**Example Client (Python):**
```python
import asyncio
import websockets
import message_pb2

async with websockets.connect("ws://localhost:8080/ws/websocketmessages/events.>") as ws:
    while True:
        frame_bytes = await ws.recv()
        frame = message_pb2.WebSocketFrame()
        frame.ParseFromString(frame_bytes)
        # Handle frame.message or frame.control
```

#### Stream from Durable Consumer
```
WS /ws/websocketmessages/{stream}/consumer/{consumerName}
```

Connects to a pre-configured durable consumer for persistent message tracking.

**Prerequisites:**
- Consumer must be created beforehand using NATS CLI or management API
- Consumer configuration determines message delivery behavior

**Example:**
```bash
# First, create a durable consumer
nats consumer add EVENTS my-durable-consumer \
  --filter events.> \
  --deliver all \
  --ack none

# Then connect via WebSocket
wscat -c "ws://localhost:8080/ws/websocketmessages/EVENTS/consumer/my-durable-consumer"
```

**Message Types:**

Control messages (subscription acknowledgments, errors, keepalives):
```json
{
  "type": "CONTROL",
  "control": {
    "type": "SUBSCRIBE_ACK",
    "message": "Subscribed to events.>"
  }
}
```

Data messages (actual NATS messages):
```json
{
  "type": "MESSAGE",
  "message": {
    "subject": "events.test",
    "sequence": 42,
    "timestamp": "2025-11-24T22:00:00.000Z",
    "data": "{\"event_type\":\"test\",\"value\":123}",
    "size_bytes": 156
  }
}
```

**See also:**
- `Examples/WebSocketClientExample.cs` - Full C# client implementation
- `Examples/websocket_client_example.py` - Full Python client implementation
- `Examples/websocket_client_example.cpp` - Full C++ client implementation
- `Examples/http_client_example.cpp` - C++ HTTP/REST client implementation
- `Examples/README.md` - Complete guide for all examples

---

### Swagger UI
```bash
GET /swagger
```
Interactive API documentation with try-it-out functionality.

## Quick Start

### Local Development

```bash
# Navigate to project directory
cd csharp/NatsHttpGateway

# Restore dependencies
dotnet restore

# Run the gateway
dotnet run

# Gateway starts on http://localhost:5000
```

**Access:**
- API: http://localhost:5000/api/messages/events.test
- Swagger: http://localhost:5000/swagger
- Health: http://localhost:5000/health

### Docker

```bash
# Build image
docker build -t nats-http-gateway .

# Run container
docker run -p 8080:8080 \
  -e NATS_URL=nats://nats:4222 \
  nats-http-gateway
```

### Docker Compose

```bash
# Start gateway with NATS
docker-compose up -d nats-http-gateway

# View logs
docker-compose logs -f nats-http-gateway

# Test the API
curl http://localhost:8080/health
```

### Manual UAT Harness

Validate the full consumer surface area (health, stream inspection, consumer CRUD, JSON + protobuf publish/fetch, peek/reset/delete) with the interactive Python script in `tests/consumer_uat.py`.

1. **Install tooling**
  ```bash
  python -m pip install -r Examples/requirements.txt protobuf requests rich
  ```
2. **Generate protobuf stubs** (only if you plan to exercise the protobuf endpoints)
  ```bash
  python -m grpc_tools.protoc -I=Protos --python_out=Examples Protos/message.proto
  ```
3. **Override defaults** with environment variables when needed:
  - `GATEWAY_BASE_URL` (default `http://localhost:8080`)
  - `GATEWAY_STREAM`, `GATEWAY_SUBJECT`, `GATEWAY_CONSUMER`
  - `GATEWAY_AUTO_ADVANCE=true` to skip the script's pause prompts for unattended runs
4. **Run the script**
  ```bash
  python tests/consumer_uat.py
  ```
5. **Review artifacts** – Each execution writes `tests/consumer_uat_log.json` so you can archive the step-by-step transcript.

Use this harness after local changes or before deployments to ensure the HTTP gateway still exercises every consumer endpoint correctly.

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_URL` | `nats://localhost:4222` | NATS server connection URL |
| `STREAM_PREFIX` | `events` | Default stream name prefix |
| `ASPNETCORE_URLS` | `http://+:8080` | HTTP listening URLs |

#### Security Configuration (Optional)

| Variable | Default | Description |
|----------|---------|-------------|
| `JWT_KEY` | (none) | JWT signing key (enables authentication when set) |
| `JWT_ISSUER` | (none) | Expected JWT issuer |
| `JWT_AUDIENCE` | (none) | Expected JWT audience |
| `NATS_CA_FILE` | (none) | Path to CA certificate for NATS TLS |
| `NATS_CERT_FILE` | (none) | Path to client certificate for NATS mTLS |
| `NATS_KEY_FILE` | (none) | Path to client key for NATS mTLS |

> **Note:** See [SECURITY.md](SECURITY.md) for complete security configuration guide including JWT token generation and mTLS certificate setup.

### Stream Naming Convention

Streams are auto-created based on subject naming:

| Subject Pattern | Stream Name | Subject Filter |
|----------------|-------------|----------------|
| `events.test` | `events` | `events.>` |
| `events.user.login` | `events` | `events.>` |
| `payments.approved` | `payments` | `payments.>` |
| `orders.created` | `orders` | `orders.>` |

**Rule:** The first token of the subject (before `.`) becomes the stream name and retains the caller's casing. Existing uppercase streams continue to work, but newly auto-created streams will match the subject's prefix case. Set `STREAM_PREFIX` if you need a different default.

## How It Works

### Publish Flow

1. HTTP POST arrives at `/api/messages/{subject}`
2. Gateway checks if stream exists for subject
3. If not, creates stream with wildcard pattern (`subject-prefix.>`)
4. Publishes message to NATS JetStream
5. Returns acknowledgment with stream sequence number

### Fetch Flow

1. HTTP GET arrives at `/api/messages/{subject}?limit=N`
2. Gateway creates ephemeral consumer with `DeliverPolicy.LastPerSubject`
3. Fetches up to N messages using `FetchAsync`
4. Deletes ephemeral consumer
5. Returns messages as JSON array

**Why ephemeral consumers?**
- Stateless (no cleanup required on restart)
- Scalable (each request is independent)
- Auto-cleanup (deleted after 5 seconds of inactivity)
- No consumer naming conflicts

## Use Cases

### 1. Real-time Dashboard

```javascript
// Stream live events via WebSocket
const ws = new WebSocket('ws://gateway:8080/ws/websocketmessages/events.>');

ws.onmessage = (event) => {
  const frame = WebSocketFrame.decode(event.data);
  if (frame.type === 'MESSAGE') {
    displayEvent(frame.message);
  }
};
```

### 2. Web Dashboard (Polling)

```javascript
// Fetch recent events for display
const response = await fetch('http://gateway:8080/api/messages/events.user.login?limit=50');
const { messages } = await response.json();
messages.forEach(msg => displayEvent(msg));
```

### 3. Webhook Integration

```bash
# Receive webhook, forward to NATS
curl -X POST http://gateway:8080/api/messages/webhooks.github \
  -H "Content-Type: application/json" \
  -d "$WEBHOOK_PAYLOAD"
```

### 4. Mobile App Backend

```bash
# Publish user action from mobile app
POST /api/messages/mobile.events
{
  "data": {
    "user_id": 12345,
    "action": "purchase",
    "amount": 29.99
  }
}
```

### 5. Legacy System Integration

Add NATS messaging to systems that only support HTTP without code changes.

## Performance Characteristics

- **Throughput:** 1000+ req/s on single instance
- **Latency:** <10ms for publish, <50ms for fetch (depends on message count)
- **Connection pooling:** Single NATS connection reused across all requests
- **Horizontal scaling:** Fully stateless, can run multiple instances behind load balancer

## Best Practices

### ✅ DO:
- Use meaningful subject hierarchies (`app.module.action`)
- Set appropriate `limit` values (avoid fetching 1000s of messages)
- Monitor health endpoint in production
- Use HTTPS in production
- Add authentication/authorization for production use

### ❌ DON'T:
- Don't poll frequently (use WebSocket endpoints for real-time streaming)
- Don't fetch all messages in one request (paginate)
- Don't create streams manually (let gateway auto-create)
- Don't use HTTP polling for high-frequency updates (use WebSocket or direct NATS client)

## Monitoring

### Health Check

```bash
# Simple check
curl http://localhost:8080/health

# With jq for readability
curl -s http://localhost:8080/health | jq .
```

### Logs

Structured JSON logging for easy parsing:

```json
{
  "Timestamp": "2025-11-24T22:00:00.000Z",
  "Level": "Information",
  "Message": "Published message to events.test in stream EVENTS, seq=42"
}
```

## Troubleshooting

### Gateway won't start

**Error:** `Failed to connect to NATS`

**Solution:** Check `NATS_URL` environment variable and ensure NATS server is running.

### 404 on fetch

**Error:** Stream not found

**Solution:** Publish at least one message to the subject first to create the stream.

### No messages returned

**Possible causes:**
- Stream is empty (publish messages first)
- Subject doesn't match (check exact subject name)
- Messages expired (check stream retention policy)

## Extending the Gateway

### Add Authentication

```csharp
// In Program.cs
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/messages/{subject}", ...)
   .RequireAuthorization();
```

### Add Rate Limiting

```csharp
builder.Services.AddRateLimiter(options => {
    options.AddFixedWindowLimiter("api", options => {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
    });
});

app.UseRateLimiter();
```

## Related Components

- **Publisher** - Legacy NATS.Client 1.x publisher
- **Subscriber** - NATS.Net 2.x JetStream subscriber with history replay
- **PaymentPublisher-JetStream** - Payment simulator with JetStream
- **MessageLogger-JetStream** - Monitoring with historical replay

## License

Part of the NATS PubSub demonstration project.
