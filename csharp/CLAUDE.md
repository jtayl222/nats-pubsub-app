# NatsHttpGateway

A C# ASP.NET Core HTTP gateway for NATS JetStream, providing REST and WebSocket APIs for publishing and consuming messages.

## API Reference

### Health Controller
**Route:** `/health`

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Health check - returns NATS connection status, JetStream availability |

### Messages Controller
**Route:** `/api/messages`

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/messages/{subject}` | Publish a message to a NATS subject |
| GET | `/api/messages/{subjectFilter}` | Fetch messages using ephemeral consumer (supports wildcards like `events.>`) |
| GET | `/api/messages/{stream}/consumer/{consumerName}` | Fetch messages from a durable consumer |

**Query Parameters (GET):**
- `limit` - Max messages to retrieve (1-100, default: 10)
- `timeout` - Timeout in seconds (1-30, default: 5)

### Streams Controller
**Route:** `/api/streams`

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/streams` | List all JetStream streams |
| GET | `/api/streams/{name}` | Get stream info and statistics |
| GET | `/api/streams/{name}/subjects` | Get distinct subjects with message counts |

### Consumers Controller
**Route:** `/api/consumers`

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/consumers/templates` | Get predefined consumer templates |
| POST | `/api/consumers/{stream}` | Create a new consumer on a stream |
| GET | `/api/consumers/{stream}` | List all consumers for a stream |
| GET | `/api/consumers/{stream}/{consumer}` | Get consumer details |
| DELETE | `/api/consumers/{stream}/{consumer}` | Delete a consumer |
| GET | `/api/consumers/{stream}/{consumer}/health` | Check consumer health status |
| GET | `/api/consumers/{stream}/{consumer}/messages` | Peek messages without acknowledging |
| POST | `/api/consumers/{stream}/{consumer}/reset` | Reset/replay consumer messages |
| GET | `/api/consumers/{stream}/{consumer}/metrics/history` | Get consumer metrics history |

### Protobuf Messages Controller
**Route:** `/api/proto/protobufmessages`

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/proto/protobufmessages/{subject}` | Publish protobuf message |
| GET | `/api/proto/protobufmessages/{subject}` | Fetch messages in protobuf format |
| POST | `/api/proto/protobufmessages/{subject}/user-event` | Publish UserEvent protobuf |
| POST | `/api/proto/protobufmessages/{subject}/payment-event` | Publish PaymentEvent protobuf |

**Content-Type:** `application/x-protobuf`

### WebSocket Messages Controller
**Route:** `/ws/websocketmessages`

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/ws/websocketmessages/{subjectFilter}` | Stream messages via WebSocket (ephemeral consumer) |
| GET | `/ws/websocketmessages/{stream}/consumer/{consumerName}` | Stream from durable consumer via WebSocket |

WebSocket frames are protobuf-encoded (`WebSocketFrame` with `FrameType.Message` or `FrameType.Control`).

## Key Services

- `INatsService` - Core service interface for NATS operations (defined in `Services/`)
- Protobuf messages defined in `Protos/` directory

## Project Structure

```
NatsHttpGateway/
├── Controllers/          # API controllers
├── Models/              # Request/response DTOs
├── Services/            # NATS service implementation
└── Protos/              # Protocol Buffer definitions

NatsHttpGateway.ComponentTests/
├── README.md                      # Component test documentation
├── NatsComponentTestBase.cs       # Base class with test infrastructure
├── HealthEndpointComponentTests.cs
└── MessagesEndpointComponentTests.cs
```

## Testing

### Component Tests

Requires access to a NATS JetStream server. Uses GitLab CI services for pipeline execution.

**Verification Strategy:** Tests publish via HTTP API, then verify by reading directly from NATS using the C# NATS.Client library.

```bash
# Using the test script (recommended - handles NATS container lifecycle)
./scripts/test-gitlab-ci-local.sh component-test

# Run all stages (build, unit-test, component-test)
./scripts/test-gitlab-ci-local.sh all

# Manual execution (requires NATS running)
NATS_URL=nats://localhost:4222 dotnet test NatsHttpGateway.ComponentTests
```

**Environment Variables:**
| Variable | Description |
|----------|-------------|
| `NATS_URL` | NATS server URL (default: `nats://localhost:4222`) |
| `GATEWAY_JWT_TOKEN` | Optional JWT token for authenticated NATS connections |

**Test Conventions:**
- Each test gets a unique stream name (`TEST_{guid}`) for isolation
- Streams are cleaned up in `TearDown`
- Tests use shared models from `NatsHttpGateway.Models`
- Direct NATS verification confirms messages are correctly stored
