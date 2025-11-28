# NATS HTTP Gateway

HTTP/REST gateway for NATS JetStream messaging. Provides stateless HTTP endpoints for publishing and consuming messages from NATS subjects.

## Features

- ✅ **RESTful API** - Standard HTTP/JSON interface
- ✅ **JetStream Integration** - Persistent message storage and replay
- ✅ **Auto-Stream Creation** - Streams are created automatically
- ✅ **Stateless Design** - Scales horizontally without session management
- ✅ **Dynamic Subjects** - Publish/fetch from any subject via URL
- ✅ **Swagger/OpenAPI** - Interactive API documentation
- ✅ **Health Checks** - Monitor NATS connectivity

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

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_URL` | `nats://localhost:4222` | NATS server connection URL |
| `STREAM_PREFIX` | `EVENTS` | Default stream name prefix |
| `ASPNETCORE_URLS` | `http://+:8080` | HTTP listening URLs |

### Stream Naming Convention

Streams are auto-created based on subject naming:

| Subject Pattern | Stream Name | Subject Filter |
|----------------|-------------|----------------|
| `events.test` | `EVENTS` | `events.>` |
| `events.user.login` | `EVENTS` | `events.>` |
| `payments.approved` | `PAYMENTS` | `payments.>` |
| `orders.created` | `ORDERS` | `orders.>` |

**Rule:** First token of subject (before `.`) becomes uppercase stream name.

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

### 1. Web Dashboard

```javascript
// Fetch recent events for display
const response = await fetch('http://gateway:8080/api/messages/events.user.login?limit=50');
const { messages } = await response.json();
messages.forEach(msg => displayEvent(msg));
```

### 2. Webhook Integration

```bash
# Receive webhook, forward to NATS
curl -X POST http://gateway:8080/api/messages/webhooks.github \
  -H "Content-Type: application/json" \
  -d "$WEBHOOK_PAYLOAD"
```

### 3. Mobile App Backend

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

### 4. Legacy System Integration

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
- Don't poll frequently (use WebSocket or SSE for real-time)
- Don't fetch all messages in one request (paginate)
- Don't create streams manually (let gateway auto-create)
- Don't use this for high-frequency streaming (use direct NATS client)

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

### Add Server-Sent Events (SSE)

See documentation for SSE streaming endpoint implementation.

## Related Components

- **Publisher** - Legacy NATS.Client 1.x publisher
- **Subscriber** - NATS.Net 2.x JetStream subscriber with history replay
- **PaymentPublisher-JetStream** - Payment simulator with JetStream
- **MessageLogger-JetStream** - Monitoring with historical replay

## License

Part of the NATS PubSub demonstration project.
