# NatsHttpGateway Client Examples

This directory contains example clients demonstrating how to interact with the NATS HTTP Gateway using both HTTP/REST and WebSocket endpoints in multiple programming languages.

## Quick Start by Language

Choose your language for detailed setup instructions:

- **[Python Examples →](README-PYTHON.md)** - Python HTTP/REST and WebSocket clients
- **[C++ Examples →](README-CPP.md)** - C++ HTTP/REST and WebSocket clients
- **[C# Examples →](README-CSHARP.md)** - C# HTTP/REST and WebSocket clients

## Examples Overview

| File | Language | Protocol | Description |
|------|----------|----------|-------------|
| `protobuf_client_example.py` | Python | HTTP/REST | Protobuf message publishing and fetching |
| `websocket_client_example.py` | Python | WebSocket | Real-time message streaming |
| `http_client_example.cpp` | C++ | HTTP/REST | Protobuf message publishing and fetching |
| `websocket_client_example.cpp` | C++ | WebSocket | Real-time message streaming |
| `ProtobufClientExample.cs` | C# | HTTP/REST | Protobuf message publishing and fetching |
| `WebSocketClientExample.cs` | C# | WebSocket | Real-time message streaming |

## Prerequisites

All examples require:
- **NATS server** running (default: `nats://localhost:4222`)
- **NatsHttpGateway** running (default: `http://localhost:5000`)

## Configuration

All examples use this configuration priority:
1. **Command-line argument** (e.g., `python3 client.py http://localhost:8080`)
2. **Environment variable** (`NATS_GATEWAY_URL`)
3. **Default value** (`http://localhost:5000`)

See **[Configuration Guide](README-CONFIG.md)** for detailed setup instructions, environment-specific examples, and troubleshooting.

**Quick setup:**
```bash
# Use default (localhost:5000)
python3 protobuf_client_example.py

# Use environment variable
export NATS_GATEWAY_URL="http://localhost:8080"
python3 protobuf_client_example.py

# Use command-line argument
python3 protobuf_client_example.py http://gateway:8080
```

### Starting the Gateway

```bash
cd /path/to/NatsHttpGateway
dotnet run
```

### Verifying Setup

```bash
# Check NATS is running
nats server check

# Check gateway is running
curl http://localhost:8080/health
```

## WebSocket Frame Format

All WebSocket examples use Protocol Buffers binary frames:

```protobuf
message WebSocketFrame {
  FrameType type = 1;  // MESSAGE or CONTROL
  oneof payload {
    StreamMessage message = 2;  // NATS message data
    ControlMessage control = 3;  // Control/status messages
  }
}
```

### Control Messages

- `SUBSCRIBE_ACK` - Subscription confirmed
- `ERROR` - Error occurred
- `CLOSE` - Connection closing
- `KEEPALIVE` - Heartbeat

### Stream Messages

Contains:
- `subject` - NATS subject
- `sequence` - JetStream sequence number
- `timestamp` - Message timestamp
- `data` - Message payload (binary)
- `size_bytes` - Payload size
- `consumer` - Consumer name (for durable consumers)

## Common Workflows

### 1. Basic Publish and Fetch (HTTP)

```bash
# Terminal 1: Start the gateway
cd /path/to/NatsHttpGateway
dotnet run

# Terminal 2: Run client (choose your language)
cd Examples
python3 protobuf_client_example.py        # Python
./http_client                              # C++
# (See C# guide for C# instructions)
```

### 2. Real-time Streaming (WebSocket)

```bash
# Terminal 1: Start the gateway
cd /path/to/NatsHttpGateway
dotnet run

# Terminal 2: Start subscriber (choose your language)
cd Examples
python3 websocket_client_example.py       # Python
./websocket_client ws://localhost:8080    # C++
# (See C# guide for C# instructions)

# Terminal 3: Publish messages to see real-time streaming
python3 protobuf_client_example.py        # Python
./http_client                              # C++
```

### 3. Testing with wscat

```bash
# Install wscat
npm install -g wscat

# Connect to ephemeral consumer
wscat -c "ws://localhost:8080/ws/websocketmessages/events.>"

# In another terminal, publish messages
curl -X POST http://localhost:8080/api/messages/events.test \
  -H "Content-Type: application/json" \
  -d '{"data": {"test": "message"}}'
```

## Durable Consumers

To use durable consumer examples, first create a consumer:

```bash
nats consumer add EVENTS my-durable-consumer \
  --filter events.> \
  --deliver all \
  --ack none
```

Then connect via WebSocket:
```bash
# Endpoint format: /ws/websocketmessages/{stream}/consumer/{name}
wscat -c "ws://localhost:8080/ws/websocketmessages/EVENTS/consumer/my-durable-consumer"
```

## Troubleshooting

### Connection refused / Gateway not responding

1. Check gateway is running: `curl http://localhost:8080/health`
2. Check NATS is running: `nats server check`
3. Verify URL matches gateway configuration

### WebSocket: No messages received

The WebSocket examples stream **new** messages only (ephemeral consumers use `DeliverPolicy.New`). To see messages:

1. Connect the WebSocket client first
2. Then publish messages using the HTTP client or curl
3. Messages published before connecting won't appear

For historical messages, use:
- HTTP fetch endpoints
- Durable consumers with `DeliverPolicy.All`

## Language-Specific Guides

For detailed setup, build instructions, and troubleshooting:

- **[Python Guide](README-PYTHON.md)** - Virtual environments, pip dependencies, protobuf generation
- **[C++ Guide](README-CPP.md)** - Build systems (Make/CMake), dependencies (Boost, libcurl)
- **[C# Guide](README-CSHARP.md)** - .NET SDK, project integration

## Additional Resources

- [Main Gateway README](../README.md) - Gateway architecture and API documentation
- [Protobuf Guide](../PROTOBUF_GUIDE.md) - Protobuf endpoint details
- [Consumer Guide](../CONSUMER_README.md) - Durable consumer setup and configuration

## Support

For issues or questions:
- Check language-specific guides for common problems
- Review example code comments
- Verify all prerequisites are met
- Check the main repository documentation
