# Protobuf Client Examples

This directory contains example client code demonstrating how to use the NatsHttpGateway protobuf endpoints.

## Files

- `ProtobufClientExample.cs` - C# example client
- `protobuf_client_example.py` - Python example client
- `message_pb2.py` - Generated Python protobuf code (auto-generated)

## Prerequisites

### For Python Examples

1. Install Python 3.7+
2. Install dependencies:
   ```bash
   pip install protobuf requests
   ```

3. Generate Python protobuf code (if not already generated):
   ```bash
   cd ../NatsHttpGateway
   protoc --proto_path=Protos --python_out=../Examples Protos/message.proto
   ```

### For C# Examples

The C# example is embedded in the NatsHttpGateway project and uses the generated protobuf classes automatically.

## Running the Examples

### Python Client

```bash
# Make sure NatsHttpGateway is running first!
# Then run the Python client:
cd Examples
./protobuf_client_example.py

# Or specify a custom URL:
./protobuf_client_example.py http://localhost:8080
```

### C# Client

The C# example code is in `ProtobufClientExample.cs`. To use it:

1. Copy the code into a new Console App project
2. Add reference to NatsHttpGateway project (for protobuf types)
3. Add NuGet package: `Google.Protobuf`
4. Run the application

Or integrate the code into your own C# application.

## What the Examples Demonstrate

1. **Publishing Generic Messages**
   - Creating a `PublishMessage` with metadata
   - Serializing to protobuf binary
   - Sending HTTP POST with `application/x-protobuf`
   - Parsing `PublishAck` response

2. **Publishing Domain-Specific Events**
   - `UserEvent` - User lifecycle events
   - `PaymentEvent` - Payment transaction events
   - Using strongly-typed protobuf messages

3. **Fetching Messages**
   - HTTP GET with `Accept: application/x-protobuf`
   - Parsing `FetchResponse` with multiple messages
   - Deserializing message data

## Example Output

```
Protobuf Python Client - Connecting to http://localhost:8080
============================================================

=== Example 1: Publishing Generic Message ===
Protobuf payload size: 98 bytes
✓ Published successfully!
  Stream: EVENTS
  Sequence: 1
  Subject: events.test

=== Example 2: Publishing UserEvent ===
✓ UserEvent published!
  User ID: user-a3f8b12c
  Event Type: created
  Stream: EVENTS, Sequence: 2

=== Example 3: Publishing PaymentEvent ===
✓ PaymentEvent published!
  Transaction ID: txn-4f7c2e9a1b3d
  Amount: $99.99 USD
  Status: approved
  Stream: PAYMENTS, Sequence: 1

=== Example 4: Fetching Messages (events.test) ===
✓ Fetched 2 messages from EVENTS
  Subject: events.test
  Messages:
    [1] events.test
        Size: 45 bytes
        Time: 2025-11-29 12:30:15
        Data: {"message": "Hello from Python!"}
    [2] events.user.created
        Size: 87 bytes
        Time: 2025-11-29 12:30:16
        Data: [binary, 87 bytes]

============================================================
✓ All examples completed successfully!
```

## Payload Size Comparison

Protobuf vs JSON for the same message:

| Format | Size | Savings |
|--------|------|---------|
| JSON | 156 bytes | - |
| Protobuf | 78 bytes | 50% |

Protobuf is significantly more efficient for binary data transfer!

## Troubleshooting

### Error: "message_pb2 not found"
Generate the Python protobuf code:
```bash
cd ../NatsHttpGateway
protoc --python_out=../Examples --proto_path=Protos Protos/message.proto
```

### Error: "Could not connect"
Make sure NatsHttpGateway is running:
```bash
cd ../NatsHttpGateway
dotnet run
```

### Error: "Invalid protobuf format"
Ensure you're using the correct .proto schema version. Regenerate the code if needed.

## Further Reading

- See `../NatsHttpGateway/PROTOBUF_GUIDE.md` for detailed protobuf usage
- See `../NatsHttpGateway/Protos/message.proto` for the schema definition
- Protocol Buffers documentation: https://protobuf.dev/
