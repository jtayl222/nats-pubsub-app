# Component Testing for NatsHttpGateway

**Space:** Engineering
**Labels:** `testing`, `nats`, `jetstream`, `component-tests`, `nats-http-gateway`

---

## Overview

This guide helps you design and implement effective component tests for the NatsHttpGateway application. Unlike the existing unit tests that mock `INatsService`, component tests validate the application against a real NATS JetStream server, catching integration issues that mocks cannot reveal.

**Target Audience:** Intermediate software engineers familiar with unit testing who are implementing component tests for NatsHttpGateway.

**What You'll Learn:**
- When to use component tests vs. unit tests
- Test infrastructure setup for NatsHttpGateway
- Design considerations for each test category
- Common pitfalls and how to avoid them

---

## Understanding the Existing Test Structure

NatsHttpGateway already has comprehensive unit tests in `NatsHttpGateway.Tests/Controllers/`:

| Test File | Coverage |
|-----------|----------|
| `HealthControllerTests.cs` | Health endpoint responses |
| `MessagesControllerTests.cs` | Message publish/fetch operations |
| `StreamsControllerTests.cs` | Stream listing and info |
| `ConsumersControllerTests.cs` | Consumer CRUD and management |
| `WebSocketMessagesControllerTests.cs` | WebSocket endpoint structure |
| `ProtobufMessagesControllerTests.cs` | Protobuf message handling |

These tests mock `INatsService` to verify controller logic. Component tests will use the real `NatsService` implementation against a live NATS server.

---

## Unit Tests vs. Component Tests: Know the Difference

| Aspect | Unit Tests (Existing) | Component Tests (New) |
|--------|----------------------|----------------------|
| **Dependencies** | `Mock<INatsService>` | Real NATS JetStream server |
| **What's tested** | Controller logic, validation, error handling | Full request flow through `NatsService` to NATS |
| **Speed** | Milliseconds | Seconds |
| **What they catch** | Logic errors, edge cases | Integration issues, protocol mismatches, configuration problems |

**Key Insight:** The existing unit tests verify that *if NATS behaves as the mocks assume, the controllers respond correctly*. Component tests verify that *NATS actually behaves as the mocks assume*.

---

## Test Infrastructure Setup

### Base Test Fixture for NatsHttpGateway

Create a reusable base class that manages both the web application and NATS connectivity:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Component;

[TestFixture]
[Category("Component")]
public abstract class NatsHttpGatewayComponentTestBase
{
    protected WebApplicationFactory<Program> Factory = null!;
    protected HttpClient Client = null!;
    protected NatsConnection NatsConnection = null!;
    protected INatsJSContext JetStream = null!;
    protected string TestStreamName = null!;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        var natsUrl = Environment.GetEnvironmentVariable("NATS_URL")
            ?? "nats://localhost:4222";

        // Configure the web application to use the test NATS server
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Nats:Url", natsUrl);
            });

        Client = Factory.CreateClient();

        // Direct NATS connection for test setup/verification
        NatsConnection = new NatsConnection(new NatsOpts { Url = natsUrl });
        await NatsConnection.ConnectAsync();
        JetStream = new NatsJSContext(NatsConnection);
    }

    [SetUp]
    public void TestSetup()
    {
        // Unique stream per test ensures isolation
        TestStreamName = $"TEST_{Guid.NewGuid():N}";
    }

    [TearDown]
    public async Task TestTeardown()
    {
        try
        {
            await JetStream.DeleteStreamAsync(TestStreamName);
        }
        catch (NatsJSApiException) { /* Stream may not exist */ }
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await NatsConnection.DisposeAsync();
    }

    /// <summary>
    /// Helper for eventual consistency - retries a condition until it passes or times out.
    /// Use this when assertions may need to wait for NATS to propagate state.
    /// </summary>
    protected async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition not met within timeout period");
    }

    /// <summary>
    /// Gets the WebSocket URI for the test server.
    /// </summary>
    protected Uri GetWebSocketUri(string path)
    {
        var baseUri = Factory.Server.BaseAddress;
        var wsScheme = baseUri.Scheme == "https" ? "wss" : "ws";
        return new Uri($"{wsScheme}://{baseUri.Host}:{baseUri.Port}{path}");
    }
}
```

**Why unique streams per test?**

Using a shared stream creates hidden dependencies between tests. Test A might leave messages that cause Test B to fail. Unique streams ensure isolation, even when running tests in parallel.

---

## 1. Stream Creation/Deletion Tests

### What to Validate

Test the `/streams` endpoints against real NATS:
- Stream configuration is applied correctly
- Stream info returns accurate data
- Subject filtering works as configured
- Error responses match expected formats

### NatsHttpGateway-Specific Implementation

```csharp
[TestFixture]
[Category("Component")]
public class StreamsEndpointComponentTests : NatsHttpGatewayComponentTestBase
{
    [Test]
    public async Task ListStreams_ReturnsStreamsFromNats()
    {
        // Arrange - Create stream directly via NATS
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "test.>" }));

        // Act - Query via NatsHttpGateway API
        var response = await Client.GetAsync("/streams");
        var content = await response.Content.ReadFromJsonAsync<StreamListResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(content!.Streams.Any(s => s.Name == TestStreamName), Is.True);
    }

    [Test]
    public async Task GetStream_ReturnsAccurateStreamInfo()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "info.>" })
        {
            MaxMsgs = 1000,
            Storage = StreamConfigStorage.Memory
        });

        // Publish some messages
        for (int i = 0; i < 5; i++)
        {
            await JetStream.PublishAsync("info.test", $"message-{i}"u8.ToArray());
        }

        // Act
        var response = await Client.GetAsync($"/streams/{TestStreamName}");
        var content = await response.Content.ReadFromJsonAsync<StreamSummary>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(content!.Name, Is.EqualTo(TestStreamName));
        Assert.That(content.Messages, Is.EqualTo(5));
    }

    [Test]
    public async Task GetStream_NonexistentStream_Returns500WithError()
    {
        // Act
        var response = await Client.GetAsync("/streams/DOES_NOT_EXIST");

        // Assert - Based on existing controller behavior
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public async Task GetStreamSubjects_ReturnsSubjectBreakdown()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "subjects.>" }));

        await JetStream.PublishAsync("subjects.orders", "order1"u8.ToArray());
        await JetStream.PublishAsync("subjects.orders", "order2"u8.ToArray());
        await JetStream.PublishAsync("subjects.users", "user1"u8.ToArray());

        // Act
        var response = await Client.GetAsync($"/streams/{TestStreamName}/subjects");
        var content = await response.Content.ReadFromJsonAsync<StreamSubjectsResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(content!.Count, Is.EqualTo(2)); // Two distinct subjects
    }
}
```

### Common Pitfalls

| Pitfall | Why It Matters | Solution |
|---------|---------------|----------|
| Hardcoded stream names | Tests conflict in parallel | Use `Guid.NewGuid()` in stream names |
| Not cleaning up streams | Artifacts affect subsequent runs | Delete in `[TearDown]` |
| Assuming immediate consistency | JetStream may have slight delays | Use `WaitForAsync()` helper for retrying assertions |

---

## 2. Message Publish and Consume Round-Trip

### What to Validate

Test the `/messages` endpoints:
- Messages published via API arrive in NATS
- Messages fetched via API match what was published
- JSON serialization/deserialization works correctly
- Headers are preserved

### NatsHttpGateway-Specific Implementation

```csharp
[TestFixture]
[Category("Component")]
public class MessagesEndpointComponentTests : NatsHttpGatewayComponentTestBase
{
    [Test]
    public async Task PublishMessage_StoresInJetStream()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "publish.>" }));

        var publishRequest = new PublishRequest
        {
            Data = new { orderId = 123, amount = 99.99 }
        };

        // Act - Publish via API
        var response = await Client.PostAsJsonAsync($"/messages/publish.orders", publishRequest);
        var result = await response.Content.ReadFromJsonAsync<PublishResponse>();

        // Assert - API response
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Published, Is.True);
        Assert.That(result.Stream, Is.EqualTo(TestStreamName));
        Assert.That(result.Sequence, Is.GreaterThan(0));

        // Assert - Message actually in NATS
        var consumer = await JetStream.CreateOrUpdateConsumerAsync(
            TestStreamName,
            new ConsumerConfig("verify-consumer")
        );
        var msg = await consumer.NextAsync<byte[]>();

        Assert.That(msg, Is.Not.Null);
        var payload = JsonSerializer.Deserialize<JsonElement>(msg!.Data!);
        Assert.That(payload.GetProperty("orderId").GetInt32(), Is.EqualTo(123));
    }

    [Test]
    public async Task FetchMessages_ReturnsPublishedMessages()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "fetch.>" }));

        // Publish directly to NATS
        for (int i = 0; i < 3; i++)
        {
            await JetStream.PublishAsync("fetch.test",
                JsonSerializer.SerializeToUtf8Bytes(new { index = i }));
        }

        // Act - Fetch via API
        var response = await Client.GetAsync($"/messages/fetch.test?limit=10");
        var result = await response.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Count, Is.EqualTo(3));
        Assert.That(result.Messages, Has.Count.EqualTo(3));
        Assert.That(result.Messages[0].Sequence, Is.LessThan(result.Messages[1].Sequence));
    }

    [Test]
    public async Task FetchMessages_InvalidLimit_ReturnsBadRequest()
    {
        // Act
        var response = await Client.GetAsync("/messages/test.subject?limit=0");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task FetchMessagesFromConsumer_ReturnsConsumerMessages()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "consumer.>" }));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("test-consumer")
        {
            DeliverPolicy = ConsumerConfigDeliverPolicy.All
        });

        await JetStream.PublishAsync("consumer.test", "message-1"u8.ToArray());
        await JetStream.PublishAsync("consumer.test", "message-2"u8.ToArray());

        // Act
        var response = await Client.GetAsync(
            $"/messages/stream/{TestStreamName}/consumer/test-consumer?limit=10");
        var result = await response.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task FetchMessagesFromConsumer_NonexistentConsumer_Returns404()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "notfound.>" }));

        // Act
        var response = await Client.GetAsync(
            $"/messages/stream/{TestStreamName}/consumer/nonexistent?limit=10");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
```

### Key Design Decisions

**Why test in both directions?**

1. **API to NATS**: Publish via `/messages/{subject}`, verify message exists in NATS directly
2. **NATS to API**: Publish directly to NATS, fetch via `/messages/{subject}`

This catches issues in both the publish and consume paths.

---

## 3. Consumer Acknowledgment and Replay Behavior

### What to Validate

Test the `/streams/{stream}/consumers` endpoints:
- Consumer creation with various configurations
- Message acknowledgment advances consumer position
- Unacknowledged messages are redelivered
- Consumer deletion works correctly

### NatsHttpGateway-Specific Implementation

```csharp
[TestFixture]
[Category("Component")]
public class ConsumersEndpointComponentTests : NatsHttpGatewayComponentTestBase
{
    [Test]
    public async Task CreateConsumer_CreatesInNats()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "create.>" }));

        var request = new CreateConsumerRequest
        {
            Name = "api-created-consumer",
            Durable = true,
            DeliverPolicy = "all",
            AckPolicy = "explicit"
        };

        // Act
        var response = await Client.PostAsJsonAsync(
            $"/streams/{TestStreamName}/consumers", request);
        var result = await response.Content.ReadFromJsonAsync<ConsumerDetails>();

        // Assert - API response
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(result!.Name, Is.EqualTo("api-created-consumer"));

        // Assert - Consumer exists in NATS
        var consumer = await JetStream.GetConsumerAsync(TestStreamName, "api-created-consumer");
        Assert.That(consumer, Is.Not.Null);
    }

    [Test]
    public async Task ListConsumers_ReturnsAllConsumers()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "list.>" }));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("consumer-1"));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("consumer-2"));

        // Act
        var response = await Client.GetAsync($"/streams/{TestStreamName}/consumers");
        var result = await response.Content.ReadFromJsonAsync<ConsumerListResult>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Count, Is.EqualTo(2));
        Assert.That(result.Consumers.Select(c => c.Name),
            Is.EquivalentTo(new[] { "consumer-1", "consumer-2" }));
    }

    [Test]
    public async Task DeleteConsumer_RemovesFromNats()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "delete.>" }));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("to-delete"));

        // Act
        var response = await Client.DeleteAsync(
            $"/streams/{TestStreamName}/consumers/to-delete");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify consumer no longer exists
        var ex = Assert.ThrowsAsync<NatsJSApiException>(async () =>
        {
            await JetStream.GetConsumerAsync(TestStreamName, "to-delete");
        });
        Assert.That(ex.Error.Code, Is.EqualTo(404));
    }

    [Test]
    public async Task GetConsumerHealth_ReturnsAccurateMetrics()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "health.>" }));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("health-check"));

        // Publish messages to create lag
        for (int i = 0; i < 10; i++)
        {
            await JetStream.PublishAsync("health.test", $"msg-{i}"u8.ToArray());
        }

        // Act
        var response = await Client.GetAsync(
            $"/streams/{TestStreamName}/consumers/health-check/health");
        var result = await response.Content.ReadFromJsonAsync<ConsumerHealthResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.PendingMessages, Is.EqualTo(10));
    }

    [Test]
    public async Task FetchAndAck_AdvancesConsumerPosition()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "ack.>" }));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("ack-test")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        });

        await JetStream.PublishAsync("ack.test", "message-1"u8.ToArray());
        await JetStream.PublishAsync("ack.test", "message-2"u8.ToArray());

        // Act - First fetch (messages are auto-acked by the fetch endpoint)
        var firstFetch = await Client.GetAsync(
            $"/messages/stream/{TestStreamName}/consumer/ack-test?limit=1");
        var firstResult = await firstFetch.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Second fetch should get next message
        var secondFetch = await Client.GetAsync(
            $"/messages/stream/{TestStreamName}/consumer/ack-test?limit=1");
        var secondResult = await secondFetch.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Assert
        Assert.That(firstResult!.Messages[0].Sequence, Is.LessThan(secondResult!.Messages[0].Sequence));
    }
}
```

### Critical Insight: Test Consumer Templates

The NatsHttpGateway provides consumer templates via `GET /consumers/templates`. Verify these work correctly:

```csharp
[Test]
public async Task GetConsumerTemplates_ReturnsValidTemplates()
{
    // Act
    var response = await Client.GetAsync("/consumers/templates");
    var result = await response.Content.ReadFromJsonAsync<ConsumerTemplatesResponse>();

    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    Assert.That(result!.Count, Is.GreaterThan(0));
    Assert.That(result.Templates.All(t => !string.IsNullOrEmpty(t.Name)), Is.True);
    Assert.That(result.Templates.All(t => t.Template != null), Is.True);
}
```

---

## 4. Health Endpoint with Live NATS Connection

### What to Validate

Test the `/health` endpoint:
- Reports accurate connection state
- Reports JetStream availability
- Responds quickly (health checks are called frequently)
- Returns correct NATS URL

### NatsHttpGateway-Specific Implementation

```csharp
[TestFixture]
[Category("Component")]
public class HealthEndpointComponentTests : NatsHttpGatewayComponentTestBase
{
    [Test]
    public async Task Health_WhenNatsConnected_ReturnsHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health");
        var result = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Status, Is.EqualTo("healthy"));
        Assert.That(result.NatsConnected, Is.True);
        Assert.That(result.JetStreamAvailable, Is.True);
    }

    [Test]
    public async Task Health_IncludesNatsUrl()
    {
        // Act
        var response = await Client.GetAsync("/health");
        var result = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert
        Assert.That(result!.NatsUrl, Is.Not.Null.And.Not.Empty);
        Assert.That(result.NatsUrl, Does.StartWith("nats://"));
    }

    [Test]
    public async Task Health_RespondsWithinSLA()
    {
        // Health endpoints should be fast
        var stopwatch = Stopwatch.StartNew();

        await Client.GetAsync("/health");

        stopwatch.Stop();

        // Should respond within 200ms under normal conditions
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(200));
    }

    [Test]
    public async Task Health_IncludesTimestamp()
    {
        // Act
        var before = DateTime.UtcNow;
        var response = await Client.GetAsync("/health");
        var result = await response.Content.ReadFromJsonAsync<HealthResponse>();
        var after = DateTime.UtcNow;

        // Assert
        Assert.That(result!.Timestamp, Is.GreaterThanOrEqualTo(before));
        Assert.That(result.Timestamp, Is.LessThanOrEqualTo(after));
    }
}
```

---

## 5. WebSocket Subscription with Real Message Flow

### What to Validate

Test the `/ws/messages` endpoints:
- WebSocket connection succeeds
- Messages stream in real-time
- Subject filtering works
- Consumer-based streaming works

### NatsHttpGateway-Specific Implementation

```csharp
[TestFixture]
[Category("Component")]
public class WebSocketEndpointComponentTests : NatsHttpGatewayComponentTestBase
{
    [Test]
    public async Task WebSocket_ReceivesPublishedMessages()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "ws.>" }));

        var wsClient = Factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            GetWebSocketUri("/ws/messages/ws.test"),
            CancellationToken.None
        );

        // Allow subscription to establish
        await Task.Delay(100);

        // Act - Publish message
        await JetStream.PublishAsync("ws.test", "hello-websocket"u8.ToArray());

        // Receive via WebSocket
        var buffer = new byte[4096];
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await ws.ReceiveAsync(buffer, cts.Token);

        // Assert
        Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Binary));
        Assert.That(result.Count, Is.GreaterThan(0));

        // Cleanup
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "test done", CancellationToken.None);
    }

    [Test]
    public async Task WebSocket_StreamsMultipleMessages()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "multi.>" }));

        var wsClient = Factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            GetWebSocketUri("/ws/messages/multi.>"),
            CancellationToken.None
        );

        await Task.Delay(100);

        // Act - Publish multiple messages
        for (int i = 0; i < 3; i++)
        {
            await JetStream.PublishAsync("multi.test", $"message-{i}"u8.ToArray());
        }

        // Receive messages
        var received = 0;
        var buffer = new byte[4096];
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (received < 3 && !cts.Token.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                received++;
            }
        }

        // Assert
        Assert.That(received, Is.EqualTo(3));

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task WebSocket_ConsumerStream_ReceivesMessages()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "wscon.>" }));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("ws-consumer"));

        await JetStream.PublishAsync("wscon.test", "consumer-message"u8.ToArray());

        // Act
        var wsClient = Factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            GetWebSocketUri($"/ws/messages/{TestStreamName}/consumer/ws-consumer"),
            CancellationToken.None
        );

        var buffer = new byte[4096];
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await ws.ReceiveAsync(buffer, cts.Token);

        // Assert
        Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Binary));

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task WebSocket_InvalidStream_ClosesWithError()
    {
        // Act
        var wsClient = Factory.Server.CreateWebSocketClient();
        var ws = await wsClient.ConnectAsync(
            GetWebSocketUri("/ws/messages/NONEXISTENT_STREAM/consumer/fake"),
            CancellationToken.None
        );

        var buffer = new byte[4096];
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ws.ReceiveAsync(buffer, cts.Token);

        // Assert - Connection should close with an error status
        Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Close));
        Assert.That(ws.CloseStatus, Is.Not.EqualTo(WebSocketCloseStatus.NormalClosure));
    }
}
```

### WebSocket Testing Notes

As noted in the existing `WebSocketMessagesControllerTests.cs`:

> WebSocket controllers are difficult to unit test because they require actual WebSocket connections and manage connection lifecycle asynchronously.

Component tests provide the opportunity to properly test WebSocket behavior that unit tests cannot cover.

---

## 6. Protobuf Message Handling

### What to Validate

Test the `/messages/proto` endpoints:
- Protobuf-encoded messages are stored correctly in NATS
- Messages can be retrieved and decoded
- Schema validation works as expected

### NatsHttpGateway-Specific Implementation

```csharp
[TestFixture]
[Category("Component")]
public class ProtobufEndpointComponentTests : NatsHttpGatewayComponentTestBase
{
    [Test]
    public async Task PublishProtobuf_StoresEncodedMessageInNats()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "proto.>" }));

        var protoMessage = new SampleMessage { Id = 123, Name = "Test" };
        var content = new ByteArrayContent(protoMessage.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        // Act
        var response = await Client.PostAsync($"/messages/proto/proto.test", content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify message in NATS
        var consumer = await JetStream.CreateOrUpdateConsumerAsync(
            TestStreamName, new ConsumerConfig("proto-verify"));
        var msg = await consumer.NextAsync<byte[]>();

        Assert.That(msg, Is.Not.Null);
        var decoded = SampleMessage.Parser.ParseFrom(msg!.Data!);
        Assert.That(decoded.Id, Is.EqualTo(123));
        Assert.That(decoded.Name, Is.EqualTo("Test"));
    }

    [Test]
    public async Task FetchProtobuf_ReturnsDecodedMessage()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "protofetch.>" }));

        var protoMessage = new SampleMessage { Id = 456, Name = "Fetched" };
        await JetStream.PublishAsync("protofetch.test", protoMessage.ToByteArray());

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"/messages/proto/protofetch.test?limit=1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-protobuf"));
        var response = await Client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
```

---

## 7. Consumer Reset Operations

### What to Validate

Test consumer position reset functionality:
- Reset to beginning of stream
- Reset to specific sequence number
- Reset to specific timestamp

### NatsHttpGateway-Specific Implementation

```csharp
[TestFixture]
[Category("Component")]
public class ConsumerResetComponentTests : NatsHttpGatewayComponentTestBase
{
    [Test]
    public async Task ResetConsumer_ToBeginning_RedeliversPreviousMessages()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "reset.>" }));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("reset-consumer")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        });

        // Publish and consume messages
        await JetStream.PublishAsync("reset.test", "message-1"u8.ToArray());
        await JetStream.PublishAsync("reset.test", "message-2"u8.ToArray());

        var firstFetch = await Client.GetAsync(
            $"/messages/stream/{TestStreamName}/consumer/reset-consumer?limit=2");
        Assert.That(firstFetch.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act - Reset consumer to beginning
        var resetRequest = new { DeliverPolicy = "all" };
        var resetResponse = await Client.PostAsJsonAsync(
            $"/streams/{TestStreamName}/consumers/reset-consumer/reset", resetRequest);

        // Assert
        Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Fetch again - should get same messages
        var secondFetch = await Client.GetAsync(
            $"/messages/stream/{TestStreamName}/consumer/reset-consumer?limit=2");
        var result = await secondFetch.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        Assert.That(result!.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ResetConsumer_ToSequence_StartsFromSpecificPosition()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { "seqreset.>" }));
        await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, new ConsumerConfig("seq-consumer"));

        // Publish messages and track sequence
        var ack1 = await JetStream.PublishAsync("seqreset.test", "message-1"u8.ToArray());
        var ack2 = await JetStream.PublishAsync("seqreset.test", "message-2"u8.ToArray());
        var ack3 = await JetStream.PublishAsync("seqreset.test", "message-3"u8.ToArray());

        // Act - Reset to sequence 2
        var resetRequest = new { DeliverPolicy = "by_start_sequence", OptStartSeq = ack2.Seq };
        var resetResponse = await Client.PostAsJsonAsync(
            $"/streams/{TestStreamName}/consumers/seq-consumer/reset", resetRequest);

        // Assert
        Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var fetch = await Client.GetAsync(
            $"/messages/stream/{TestStreamName}/consumer/seq-consumer?limit=10");
        var result = await fetch.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Should get messages 2 and 3 only
        Assert.That(result!.Count, Is.EqualTo(2));
        Assert.That(result.Messages[0].Sequence, Is.EqualTo(ack2.Seq));
    }
}
```

---

## Running Component Tests

### Project Structure

Component tests are now in a separate project: `NatsHttpGateway.ComponentTests`

This separation allows developers without Docker to skip component tests entirely while still running unit tests.

### In CI/CD (GitLab)

Component tests run automatically when the `component-test` stage executes:

```yaml
component-test:
  stage: component-test
  services:
    - name: nats:latest
      alias: nats
      command: ["--jetstream", "-m", "8222"]
  variables:
    NATS_URL: "nats://nats:4222"
    # JWT_TOKEN: "${NATS_JWT_TOKEN}"  # Optional: for authenticated NATS
  script:
    - dotnet test NatsHttpGateway.ComponentTests/NatsHttpGateway.ComponentTests.csproj
        --configuration Release
        --logger "trx;LogFileName=component-test-results.trx"
```

### Local Development

#### Using the Test Script (Recommended)

```bash
# Run the test script (handles Docker, NATS startup automatically)
./scripts/test-gitlab-ci-local.sh component-test

# Run all stages (build, unit-test, component-test)
./scripts/test-gitlab-ci-local.sh all

# Run only unit tests (no Docker required)
./scripts/test-gitlab-ci-local.sh unit-test
```

#### Manual Setup

```bash
# Start NATS with JetStream
docker run -d --name nats-test -p 4222:4222 -p 8222:8222 nats:latest --jetstream -m 8222

# Run component tests
cd NatsHttpGateway.ComponentTests
export NATS_URL="nats://localhost:4222"
dotnet test

# Run unit tests only (separate project)
cd ../NatsHttpGateway.Tests
dotnet test --filter "Category!=Component"

# Cleanup
docker rm -f nats-test
```

### JWT Authentication

If your NATS server requires JWT authentication, set the `JWT_TOKEN` environment variable:

```bash
# Using JWT token
export JWT_TOKEN=$(cat ~/.nats/my.jwt)
./scripts/test-gitlab-ci-local.sh component-test

# Or using an external NATS server with JWT
export NATS_URL="nats://secure-server:4222"
export JWT_TOKEN="your-jwt-token-here"
export SKIP_DOCKER_CHECK=true
./scripts/test-gitlab-ci-local.sh component-test
```

### Windows 11 Notes

For developers on Windows 11:

1. **With Docker Desktop**: Install Docker Desktop with WSL 2 backend, then run tests via Git Bash or WSL
2. **Without Docker**: Run unit tests only with `./scripts/test-gitlab-ci-local.sh unit-test`
3. **External NATS**: Use `SKIP_DOCKER_CHECK=true` and set `NATS_URL` to an external server

---

## Summary: Component Test Priority Matrix

| Test Area | Endpoints | Priority | Why |
|-----------|-----------|----------|-----|
| Message round-trip | `/messages/*` | Critical | Core functionality |
| Consumer acknowledgment | `/streams/*/consumers/*` | Critical | Data loss prevention |
| Consumer reset | `/streams/*/consumers/*/reset` | Critical | Recovery operations |
| Stream operations | `/streams/*` | High | Foundation for other features |
| Protobuf messages | `/messages/proto/*` | High | Binary protocol correctness |
| Health endpoint | `/health` | Medium | Orchestration accuracy |
| WebSocket streaming | `/ws/messages/*` | Medium | Real-time features |

---

## Related Documentation

- [NatsHttpGateway README](../README.md)
- [Existing Unit Tests](../../NatsHttpGateway.Tests/)
- [NATS.Net Documentation](https://nats-io.github.io/nats.net/)
