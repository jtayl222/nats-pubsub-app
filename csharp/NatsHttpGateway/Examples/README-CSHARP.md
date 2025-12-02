# C# Examples Guide

Complete guide for using the C# HTTP/REST and WebSocket clients with NatsHttpGateway.

## Prerequisites

- .NET 8.0 SDK or later
- NATS server running (`nats://localhost:4222`)
- NatsHttpGateway running (`http://localhost:8080`)

## Quick Start

The C# examples are included in the main NatsHttpGateway project, so they're already available when you build the gateway.

```bash
# Navigate to gateway directory
cd /path/to/NatsHttpGateway

# Build the project
dotnet build

# The examples are now compiled
# (See "Running the Examples" section below for how to use them)
```

## Setup Instructions

### 1. Verify .NET SDK

```bash
dotnet --version
# Should show 8.0.x or later
```

**If not installed:**
- Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download)
- Or use package manager:
  ```bash
  # macOS
  brew install dotnet

  # Ubuntu
  sudo apt-get install dotnet-sdk-8.0
  ```

### 2. Project Structure

The C# examples are located in the `Examples/` directory but are part of the main project:

```
NatsHttpGateway/
├── Examples/
│   ├── ProtobufClientExample.cs      # HTTP/REST client
│   ├── WebSocketClientExample.cs     # WebSocket client
│   └── (Other language examples)
├── Controllers/
├── Services/
├── Protos/
└── NatsHttpGateway.csproj
```

**Note:** The examples are **not** standalone projects. They're included in the main NatsHttpGateway project.

### 3. Build the Project

```bash
cd /path/to/NatsHttpGateway
dotnet build
```

You may see warnings about multiple entry points - this is expected since both the main Program.cs and example files contain `Main()` methods.

## Running the Examples

### HTTP/REST Client

**File:** `Examples/ProtobufClientExample.cs`

The HTTP client example demonstrates:
- Publishing generic protobuf messages
- Publishing domain-specific events (UserEvent, PaymentEvent)
- Fetching messages from subjects

**To use as a library in your code:**

```csharp
using NatsHttpGateway.Examples;

var client = new ProtobufClientExample("http://localhost:8080");

// Run all examples
await client.RunAllExamples();

// Or run specific examples
await client.PublishGenericMessage();
await client.PublishUserEvent();
await client.FetchMessages("events.test", limit: 10);
```

**Example integration:**

```csharp
using NatsHttpGateway.Examples;
using NatsHttpGateway.Protos;
using Google.Protobuf.WellKnownTypes;

class MyApp
{
    static async Task Main(string[] args)
    {
        var client = new ProtobufClientExample("http://localhost:8080");

        // Publish a custom message
        var message = new PublishMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Subject = "my.custom.subject",
            Source = "my-app",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Data = ByteString.CopyFromUtf8("{\"action\":\"login\"}")
        };
        message.Metadata.Add("version", "1.0");

        // This would require exposing individual methods or adding your own HTTP client code
        await client.PublishGenericMessage();
    }
}
```

### WebSocket Client

**File:** `Examples/WebSocketClientExample.cs`

The WebSocket client demonstrates:
- Streaming from ephemeral consumers
- Streaming from durable consumers
- Timeout-based streaming
- Parsing protobuf binary frames

**To use as a library in your code:**

```csharp
using NatsHttpGateway.Examples;

var client = new WebSocketClientExample("http://localhost:8080");

// Run all examples
await client.RunAllExamples();

// Or run specific examples
await client.StreamFromEphemeralConsumer("events.>", maxMessages: 10);
await client.StreamFromDurableConsumer("EVENTS", "my-consumer", maxMessages: 10);
await client.StreamWithTimeout("events.test", timeoutSeconds: 30);
```

**Custom WebSocket client example:**

```csharp
using System.Net.WebSockets;
using NatsHttpGateway.Protos;
using Google.Protobuf;

class MyWebSocketClient
{
    async Task StreamMessages(string subjectFilter)
    {
        var wsUrl = $"ws://localhost:8080/ws/websocketmessages/{subjectFilter}";

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

        Console.WriteLine("Connected!");

        while (ws.State == WebSocketState.Open)
        {
            var buffer = new byte[1024 * 16];
            var result = await ws.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None
            );

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                var frameBytes = new byte[result.Count];
                Array.Copy(buffer, frameBytes, result.Count);

                var frame = WebSocketFrame.Parser.ParseFrom(frameBytes);

                switch (frame.Type)
                {
                    case FrameType.Message:
                        Console.WriteLine($"Message: {frame.Message.Subject}");
                        Console.WriteLine($"Data: {frame.Message.Data.ToStringUtf8()}");
                        break;

                    case FrameType.Control:
                        Console.WriteLine($"Control: {frame.Control.Type}");
                        break;
                }
            }
        }
    }
}

// Usage
await new MyWebSocketClient().StreamMessages("events.>");
```

## Creating a Standalone Example Project

If you want to extract the examples into a separate project:

### 1. Create New Project

```bash
dotnet new console -n NatsClientExamples
cd NatsClientExamples
```

### 2. Add Required Packages

```bash
dotnet add package Google.Protobuf
dotnet add package Grpc.Tools
```

### 3. Copy Files

```bash
# Copy example files
cp /path/to/NatsHttpGateway/Examples/ProtobufClientExample.cs ./
cp /path/to/NatsHttpGateway/Examples/WebSocketClientExample.cs ./

# Copy protobuf definitions
mkdir Protos
cp /path/to/NatsHttpGateway/Protos/message.proto ./Protos/

# Copy model classes (or reference the gateway project)
cp /path/to/NatsHttpGateway/Models/*.cs ./Models/
```

### 4. Update .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.25.0" />
    <PackageReference Include="Grpc.Tools" Version="2.60.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Protos\message.proto" GrpcServices="None" />
  </ItemGroup>
</Project>
```

### 5. Create Program.cs

```csharp
using NatsClientExamples;

var choice = args.Length > 0 ? args[0] : "http";

if (choice == "http")
{
    var client = new ProtobufClientExample("http://localhost:8080");
    await client.RunAllExamples();
}
else if (choice == "ws")
{
    var client = new WebSocketClientExample("http://localhost:8080");
    await client.RunAllExamples();
}
```

### 6. Build and Run

```bash
dotnet build
dotnet run http    # Run HTTP client
dotnet run ws      # Run WebSocket client
```

## Code Examples

### Publishing with HttpClient

```csharp
using System.Net.Http.Headers;
using NatsHttpGateway.Protos;
using Google.Protobuf;

var client = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };

var message = new PublishMessage
{
    MessageId = Guid.NewGuid().ToString(),
    Subject = "events.test",
    Source = "my-app",
    Data = ByteString.CopyFromUtf8("{\"test\": \"data\"}")
};

var protobufBytes = message.ToByteArray();
var content = new ByteArrayContent(protobufBytes);
content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

var response = await client.PostAsync(
    "/api/proto/ProtobufMessages/events.test",
    content
);

var responseBytes = await response.Content.ReadAsByteArrayAsync();
var ack = PublishAck.Parser.ParseFrom(responseBytes);

Console.WriteLine($"Published to {ack.Stream}, sequence {ack.Sequence}");
```

### Fetching Messages

```csharp
var request = new HttpRequestMessage(
    HttpMethod.Get,
    "/api/proto/ProtobufMessages/events.test?limit=10"
);
request.Headers.Accept.Add(
    new MediaTypeWithQualityHeaderValue("application/x-protobuf")
);

var response = await client.SendAsync(request);
var responseBytes = await response.Content.ReadAsByteArrayAsync();
var fetchResponse = FetchResponse.Parser.ParseFrom(responseBytes);

Console.WriteLine($"Fetched {fetchResponse.Count} messages");
foreach (var msg in fetchResponse.Messages)
{
    Console.WriteLine($"  [{msg.Sequence}] {msg.Subject}");
    Console.WriteLine($"    Data: {msg.Data.ToStringUtf8()}");
}
```

## Troubleshooting

### Build Warnings: Multiple entry points

**Warning:** `CS7022: The entry point of the program is global code; ignoring 'Program.Main(string[])' entry point.`

**Cause:** The Examples directory contains classes with `Main()` methods, but the gateway uses top-level statements in Program.cs.

**Solution:** This is expected and harmless. The warning can be ignored, or you can:
1. Extract examples to a separate project (see above)
2. Remove or rename the `Main()` methods in example files
3. Suppress the warning in .csproj:
   ```xml
   <PropertyGroup>
     <NoWarn>CS7022</NoWarn>
   </PropertyGroup>
   ```

### Protobuf code not generated

**Problem:** Can't find protobuf classes like `WebSocketFrame`

**Solution:**
```bash
# Clean and rebuild
dotnet clean
dotnet build

# Check generated files
ls obj/Debug/net8.0/Protos/
# Should contain Message.cs
```

### NullReferenceException with protobuf fields

**Problem:** Getting null reference errors when accessing protobuf fields

**Solution:** Use the `Has*` methods or null-conditional operators:

```csharp
// Wrong
var timestamp = message.Timestamp.Seconds;  // May throw if not set

// Right
if (message.HasTimestamp)
{
    var timestamp = message.Timestamp.Seconds;
}

// Or
var timestamp = message.Timestamp?.Seconds ?? 0;
```

### WebSocket connection fails

**Problem:** Can't connect to WebSocket endpoint

**Solutions:**
1. Check gateway is running: `curl http://localhost:8080/health`
2. Verify WebSocket middleware is enabled in Program.cs:
   ```csharp
   app.UseWebSockets();
   ```
3. Check firewall settings
4. Verify URL format: `ws://` not `wss://` (unless using SSL)

## Workflow Examples

### End-to-End Test

```csharp
using NatsHttpGateway.Examples;

class Program
{
    static async Task Main()
    {
        // Start WebSocket subscriber in background
        var wsClient = new WebSocketClientExample("http://localhost:8080");
        var subscriberTask = Task.Run(async () =>
        {
            await wsClient.StreamFromEphemeralConsumer("events.>", maxMessages: 10);
        });

        // Give subscriber time to connect
        await Task.Delay(1000);

        // Publish messages
        var httpClient = new ProtobufClientExample("http://localhost:8080");
        await httpClient.RunAllExamples();

        // Wait for subscriber to finish
        await subscriberTask;
    }
}
```

### Continuous Streaming with Cancellation

```csharp
var cts = new CancellationTokenSource();

// Cancel after 30 seconds
cts.CancelAfter(TimeSpan.FromSeconds(30));

try
{
    await foreach (var message in natsService.StreamMessagesAsync("events.>", cts.Token))
    {
        Console.WriteLine($"Received: {message.Subject}");
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Streaming cancelled after timeout");
}
```

## Best Practices

1. **Dispose resources:** Use `using` statements for HttpClient and WebSocket
2. **Handle cancellation:** Always pass CancellationToken for long-running operations
3. **Error handling:** Wrap WebSocket operations in try-catch
4. **Connection management:** Reuse HttpClient instances (singleton pattern)
5. **Binary data:** Use `ByteString` for protobuf binary fields
6. **Timestamps:** Always convert to UTC: `Timestamp.FromDateTime(DateTime.UtcNow)`

## Additional Resources

- [.NET 8 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8)
- [System.Net.WebSockets](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets)
- [Google.Protobuf C# API](https://protobuf.dev/reference/csharp/api-docs/)
- [Main Examples README](README.md)

## Next Steps

- Adapt examples for your use case
- Add structured logging (ILogger)
- Implement retry policies (Polly)
- Add authentication/authorization
- Consider gRPC for better performance (if gateway supports it)
