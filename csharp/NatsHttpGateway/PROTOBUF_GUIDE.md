# Protocol Buffers Support in NatsHttpGateway

This guide explains how to use Protocol Buffers (protobuf) with the NATS HTTP Gateway.

## Overview

The gateway supports both JSON and Protocol Buffers serialization formats:
- **JSON endpoints**: `/api/Messages/{subject}`
- **Protobuf endpoints**: `/api/proto/ProtobufMessages/{subject}`

## Protobuf Schema

The protobuf schema is defined in `Protos/message.proto` and includes:

### Core Message Types
- `PublishMessage` - Message to publish to NATS
- `PublishAck` - Acknowledgment after publishing
- `FetchRequest` - Request to fetch messages
- `FetchResponse` - Response with fetched messages
- `FetchedMessage` - Individual message in fetch response

### Domain-Specific Types
- `UserEvent` - User-related events
- `PaymentEvent` - Payment-related events

## API Endpoints

### 1. Publish Protobuf Message

**Endpoint**: `POST /api/proto/ProtobufMessages/{subject}`

**Content-Type**: `application/x-protobuf`

**Request Body**: Binary protobuf `PublishMessage`

**Response**: Binary protobuf `PublishAck`

### 2. Fetch Protobuf Messages

**Endpoint**: `GET /api/proto/ProtobufMessages/{subject}?limit=10`

**Accept**: `application/x-protobuf`

**Response**: Binary protobuf `FetchResponse`

### 3. Publish UserEvent

**Endpoint**: `POST /api/proto/ProtobufMessages/{subject}/user-event`

**Content-Type**: `application/x-protobuf`

**Request Body**: Binary protobuf `UserEvent`

### 4. Publish PaymentEvent

**Endpoint**: `POST /api/proto/ProtobufMessages/{subject}/payment-event`

**Content-Type**: `application/x-protobuf`

**Request Body**: Binary protobuf `PaymentEvent`

## Usage Examples

### Using curl (with protoc)

#### 1. Compile .proto file
```bash
protoc --proto_path=Protos \
       --csharp_out=. \
       --python_out=. \
       Protos/message.proto
```

#### 2. Create and encode a message (Python example)
```python
#!/usr/bin/env python3
import message_pb2
from google.protobuf import timestamp_pb2
import requests
from datetime import datetime

# Create a PublishMessage
msg = message_pb2.PublishMessage()
msg.message_id = "user-123"
msg.subject = "events.user.created"
msg.source = "python-client"
msg.timestamp.GetCurrentTime()
msg.data = b'{"user_id": 42, "email": "test@example.com"}'
msg.metadata["client"] = "python"

# Serialize to binary
protobuf_bytes = msg.SerializeToString()

# Send to gateway
response = requests.post(
    "http://localhost:8080/api/proto/ProtobufMessages/events.user.created",
    headers={"Content-Type": "application/x-protobuf"},
    data=protobuf_bytes
)

# Parse response
ack = message_pb2.PublishAck()
ack.ParseFromString(response.content)
print(f"Published: {ack.published}, Stream: {ack.stream}, Seq: {ack.sequence}")
```

#### 3. Fetch messages
```python
# Fetch messages
response = requests.get(
    "http://localhost:8080/api/proto/ProtobufMessages/events.user.created?limit=10",
    headers={"Accept": "application/x-protobuf"}
)

# Parse response
fetch_response = message_pb2.FetchResponse()
fetch_response.ParseFromString(response.content)

print(f"Found {fetch_response.count} messages:")
for msg in fetch_response.messages:
    print(f"  - Seq: {msg.sequence}, Subject: {msg.subject}")
```

### Using C# Client

```csharp
using NatsHttpGateway.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

var client = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };

// Create a UserEvent
var userEvent = new UserEvent
{
    UserId = "user-123",
    EventType = "created",
    Email = "test@example.com",
    OccurredAt = Timestamp.FromDateTime(DateTime.UtcNow)
};
userEvent.Attributes.Add("plan", "premium");

// Serialize and send
var content = new ByteArrayContent(userEvent.ToByteArray());
content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

var response = await client.PostAsync(
    "/api/proto/ProtobufMessages/events.user.created/user-event",
    content
);

// Parse response
var ackBytes = await response.Content.ReadAsByteArrayAsync();
var ack = PublishAck.Parser.ParseFrom(ackBytes);
Console.WriteLine($"Published to stream {ack.Stream}, sequence {ack.Sequence}");
```

### Using grpcurl (for testing)

```bash
# Install grpcurl
brew install grpcurl

# Create a JSON representation of the message
echo '{
  "message_id": "test-123",
  "subject": "events.test",
  "source": "grpcurl-test",
  "data": "dGVzdCBkYXRh"
}' > message.json

# Convert JSON to protobuf and send (requires additional tooling)
# Note: grpcurl is for gRPC, for HTTP we use protoc + curl
protoc --encode=nats.messages.PublishMessage Protos/message.proto < message.json \
  | curl -X POST \
    -H "Content-Type: application/x-protobuf" \
    --data-binary @- \
    http://localhost:8080/api/proto/ProtobufMessages/events.test
```

## Benefits of Protobuf

1. **Performance**
   - Smaller payload size (~30-50% smaller than JSON)
   - Faster serialization/deserialization
   - Binary format reduces network transfer time

2. **Type Safety**
   - Strong typing enforced by schema
   - Compile-time validation
   - Backward/forward compatibility

3. **Language Support**
   - Works with C#, Python, Java, Go, JavaScript, etc.
   - Auto-generated client code
   - Consistent API across languages

4. **Schema Evolution**
   - Can add new fields without breaking existing clients
   - Field numbers ensure compatibility
   - Optional fields with defaults

## Comparison: JSON vs Protobuf

### JSON Endpoint
```bash
curl -X POST "http://localhost:8080/api/Messages/events.test" \
  -H "Content-Type: application/json" \
  -d '{
    "message_id": "test-123",
    "data": {"user_id": 42},
    "source": "curl"
  }'
```

**Payload size**: ~80 bytes

### Protobuf Endpoint
```bash
# Using encoded protobuf binary
curl -X POST "http://localhost:8080/api/proto/ProtobufMessages/events.test" \
  -H "Content-Type: application/x-protobuf" \
  --data-binary @message.pb
```

**Payload size**: ~40 bytes (50% smaller)

## Testing Protobuf Endpoints

### Using Swagger UI

Swagger UI doesn't natively support `application/x-protobuf`, so you'll need to:
1. Use the JSON endpoints in Swagger for testing
2. Use curl/Postman/code clients for protobuf testing

### Using Postman

1. Create a POST request
2. Set URL: `http://localhost:8080/api/proto/ProtobufMessages/events.test`
3. Set Header: `Content-Type: application/x-protobuf`
4. Body: Select "Binary" and upload a `.pb` file
5. Send request

### Using a .NET Console App

See `examples/ProtobufClient` for a complete C# client example.

## Troubleshooting

### Error: "Invalid protobuf format"
- Ensure you're sending properly serialized protobuf bytes
- Verify the message type matches what the endpoint expects
- Check that you're using the correct .proto schema version

### Error: "Request body is empty"
- Make sure you're sending binary data, not base64 or text
- Use `--data-binary` with curl, not `-d`
- In code, use `ByteArrayContent`, not `StringContent`

### Swagger shows "unsupported media type"
- Swagger UI doesn't support protobuf binaries
- Use curl, Postman, or code clients instead
- Or use the JSON endpoints for browser-based testing

## Next Steps

1. **Generate client code**: Run `protoc` to generate code for your language
2. **Create a client library**: Wrap the HTTP calls in a typed client
3. **Add custom message types**: Extend `message.proto` with your domain models
4. **Monitor performance**: Compare JSON vs protobuf latency and payload sizes
