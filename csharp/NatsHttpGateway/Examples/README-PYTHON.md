# Python Examples Guide

Complete guide for using the Python HTTP/REST and WebSocket clients with NatsHttpGateway.

## Prerequisites

- Python 3.8 or later
- NATS server running (`nats://localhost:4222`)
- NatsHttpGateway running (`http://localhost:8080`)

## Quick Start

```bash
# Navigate to Examples directory
cd Examples

# Create virtual environment
python3 -m venv venv

# Activate virtual environment
source venv/bin/activate  # macOS/Linux
# OR
venv\Scripts\activate     # Windows

# Install dependencies
pip install -r requirements.txt

# Generate protobuf classes
protoc --proto_path=../Protos --python_out=. message.proto

# Run HTTP client
python3 protobuf_client_example.py

# Run WebSocket client
python3 websocket_client_example.py
```

## Setup Instructions

### 1. Create Virtual Environment

Using a virtual environment isolates project dependencies from your system Python.

```bash
# Navigate to Examples directory
cd /path/to/NatsHttpGateway/Examples

# Create virtual environment
python3 -m venv venv
```

### 2. Activate Virtual Environment

**On macOS/Linux:**
```bash
source venv/bin/activate
```

**On Windows:**
```cmd
venv\Scripts\activate
```

You'll see `(venv)` prefix in your terminal when activated.

### 3. Install Dependencies

```bash
pip install -r requirements.txt
```

This installs:
- `protobuf>=4.21.0` - Protocol Buffers library
- `websockets>=11.0` - WebSocket client library

### 4. Generate Protobuf Classes

The Python examples require generated protobuf classes:

```bash
# From Examples directory
protoc --proto_path=../Protos --python_out=. message.proto
```

This generates `message_pb2.py` which contains:
- `PublishMessage`
- `PublishAck`
- `FetchRequest` / `FetchResponse`
- `UserEvent` / `PaymentEvent`
- `WebSocketFrame`
- `StreamMessage` / `ControlMessage`

## Running the Examples

### HTTP/REST Client

**File:** `protobuf_client_example.py`

```bash
# Activate venv first
source venv/bin/activate

# Run with default settings (localhost:8080)
python3 protobuf_client_example.py

# Or specify custom URL
python3 protobuf_client_example.py http://gateway:8080
```

**What it does:**
1. Publishes a generic protobuf message to `events.test`
2. Publishes a `UserEvent` to `events.user.created`
3. Publishes a `PaymentEvent` to `payments.credit_card.approved`
4. Fetches and displays recent messages

**Example output:**
```
=== Example 1: Publishing Generic Message ===
Protobuf payload size: 142 bytes
✓ Published successfully!
  Stream: EVENTS
  Sequence: 42
  Subject: events.test

=== Example 2: Publishing UserEvent ===
✓ UserEvent published!
  User ID: user-a3f2b8c4
  Event Type: created
  Stream: EVENTS, Sequence: 43
```

### WebSocket Client

**File:** `websocket_client_example.py`

```bash
# Activate venv first
source venv/bin/activate

# Run with default settings (localhost:8080)
python3 websocket_client_example.py

# Or specify custom URL
python3 websocket_client_example.py http://gateway:8080
```

**What it does:**
1. Connects to ephemeral consumer for `events.>` subject filter
2. Streams 5 messages in real-time
3. Demonstrates timeout-based streaming (10 seconds)

**Example output:**
```
=== Example 1: Streaming from Ephemeral Consumer (events.>) ===
Connecting to: ws://localhost:8080/ws/websocketmessages/events.>
✓ WebSocket connected
✓ Control [SUBSCRIBE_ACK]: Subscribed to events.>
  Message received:
    Subject:  events.test
    Sequence: 45
    Size:     156 bytes
    Time:     2025-12-01 15:30:42.123
    Data:     {"message": "Hello from Python!"}

✓ Received 5 messages
```

### Durable Consumer Example

The durable consumer example is commented out by default. To use it:

1. **Create a durable consumer:**
   ```bash
   nats consumer add EVENTS my-durable-consumer \
     --filter events.> \
     --deliver all \
     --ack none
   ```

2. **Uncomment in code:**
   ```python
   # In websocket_client_example.py, uncomment:
   await self.stream_from_durable_consumer("EVENTS", "my-durable-consumer", max_messages=5)
   ```

3. **Run:**
   ```bash
   python3 websocket_client_example.py
   ```

## Code Examples

### Publishing a Message

```python
import message_pb2
import requests
from google.protobuf.timestamp_pb2 import Timestamp

# Create message
msg = message_pb2.PublishMessage()
msg.message_id = "custom-id-123"
msg.subject = "events.test"
msg.source = "my-app"
msg.timestamp.GetCurrentTime()
msg.data = b'{"key": "value"}'
msg.metadata["client"] = "python"

# Publish
response = requests.post(
    "http://localhost:8080/api/proto/ProtobufMessages/events.test",
    headers={"Content-Type": "application/x-protobuf"},
    data=msg.SerializeToString()
)

# Parse response
ack = message_pb2.PublishAck()
ack.ParseFromString(response.content)
print(f"Published to {ack.stream}, sequence {ack.sequence}")
```

### Streaming Messages

```python
import asyncio
import websockets
import message_pb2

async def stream_messages():
    async with websockets.connect(
        "ws://localhost:8080/ws/websocketmessages/events.>"
    ) as ws:
        while True:
            frame_bytes = await ws.recv()

            frame = message_pb2.WebSocketFrame()
            frame.ParseFromString(frame_bytes)

            if frame.type == message_pb2.MESSAGE:
                msg = frame.message
                print(f"Received: {msg.subject} (seq: {msg.sequence})")
                print(f"Data: {msg.data.decode('utf-8')}")

# Run
asyncio.run(stream_messages())
```

## Troubleshooting

### ModuleNotFoundError: No module named 'websockets'

Make sure you:
1. Created the virtual environment: `python3 -m venv venv`
2. Activated it: `source venv/bin/activate`
3. Installed dependencies: `pip install -r requirements.txt`

**Check activation:**
```bash
which python3
# Should show: /path/to/Examples/venv/bin/python3
```

### ModuleNotFoundError: No module named 'message_pb2'

Generate the protobuf classes:
```bash
protoc --proto_path=../Protos --python_out=. message.proto
```

**Verify:**
```bash
ls message_pb2.py
# Should exist
```

### Connection refused

Gateway not running. Start it:
```bash
cd /path/to/NatsHttpGateway
dotnet run
```

**Verify:**
```bash
curl http://localhost:8080/health
```

### WebSocket: No messages received

Ephemeral consumers stream **new** messages only. To see messages:

1. Start WebSocket client first
2. Then publish messages in another terminal
3. Messages published before connecting won't appear

**Test:**
```bash
# Terminal 1: Start subscriber
python3 websocket_client_example.py

# Terminal 2: Publish messages
curl -X POST http://localhost:8080/api/messages/events.test \
  -H "Content-Type: application/json" \
  -d '{"data": {"test": "message"}}'
```

### protoc: command not found

Install Protocol Buffers compiler:

**macOS:**
```bash
brew install protobuf
```

**Ubuntu/Debian:**
```bash
sudo apt-get install protobuf-compiler
```

**Verify:**
```bash
protoc --version
# Should show: libprotoc 3.x.x or higher
```

### SSL/TLS errors with websockets

If using `wss://` (secure WebSocket):

```python
import ssl

ssl_context = ssl.create_default_context()
async with websockets.connect(url, ssl=ssl_context) as ws:
    # ...
```

## Workflow Examples

### End-to-End Test

```bash
# Terminal 1: Start gateway
cd /path/to/NatsHttpGateway
dotnet run

# Terminal 2: Start WebSocket subscriber
cd Examples
source venv/bin/activate
python3 websocket_client_example.py &

# Terminal 3: Publish messages
python3 protobuf_client_example.py

# You'll see messages in Terminal 2 in real-time
```

### Continuous Streaming

Modify `websocket_client_example.py` to run indefinitely:

```python
# Remove max_messages limit
while True:  # Instead of: while message_count < max_messages
    frame_bytes = await ws.recv()
    # ...
```

## Deactivating Virtual Environment

When done:

```bash
deactivate
```

## Additional Resources

- [Python asyncio documentation](https://docs.python.org/3/library/asyncio.html)
- [websockets library](https://websockets.readthedocs.io/)
- [Python Protocol Buffers](https://protobuf.dev/getting-started/pythontutorial/)
- [Main Examples README](README.md)

## Next Steps

- Adapt examples for your use case
- Add error handling for production use
- Implement reconnection logic for WebSocket
- Add authentication headers if needed
