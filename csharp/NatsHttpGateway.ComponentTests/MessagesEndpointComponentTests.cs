using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NATS.Client.JetStream.Models;
using NatsHttpGateway.Models;
using NUnit.Framework;

namespace NatsHttpGateway.ComponentTests;

/// <summary>
/// Component tests for the /api/messages endpoints.
/// Verifies publish and fetch operations against NATS JetStream.
/// </summary>
[TestFixture]
[Category("Component")]
public class MessagesEndpointComponentTests : NatsComponentTestBase
{
    [Test]
    public async Task PublishMessage_StoresMessageInJetStream()
    {
        // Arrange - Create stream for test subject
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        var publishRequest = new PublishRequest
        {
            MessageId = Guid.NewGuid().ToString(),
            Data = new { orderId = 123, amount = 99.99, customer = "test-customer" },
            Source = "component-test"
        };

        // Act - Publish via API
        var response = await Client.PostAsJsonAsync($"/api/messages/{TestStreamName}.orders", publishRequest);
        var result = await response.Content.ReadFromJsonAsync<PublishResponse>();

        // Assert - API response
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Published, Is.True);
        Assert.That(result.Stream, Is.EqualTo(TestStreamName));
        Assert.That(result.Sequence, Is.GreaterThan(0));
        Assert.That(result.Subject, Is.EqualTo($"{TestStreamName}.orders"));
    }

    [Test]
    public async Task PublishMessage_VerifyWithDirectNatsRead()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        var testData = new { productId = 456, name = "Test Product", price = 29.99 };
        var publishRequest = new PublishRequest
        {
            MessageId = "test-msg-001",
            Data = testData,
            Source = "component-test"
        };

        // Act - Publish via API
        var response = await Client.PostAsJsonAsync($"/api/messages/{TestStreamName}.products", publishRequest);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify - Read directly from NATS using our test connection
        var consumer = await JetStream.CreateOrUpdateConsumerAsync(
            TestStreamName,
            new ConsumerConfig("verify-consumer") { DeliverPolicy = ConsumerConfigDeliverPolicy.All }
        );

        var msg = await consumer.NextAsync<byte[]>();
        Assert.That(msg.HasValue, Is.True, "Message should exist in NATS");

        var payload = JsonSerializer.Deserialize<JsonElement>(msg.Value.Data!);
        Assert.That(payload.GetProperty("data").GetProperty("productId").GetInt32(), Is.EqualTo(456));
        Assert.That(payload.GetProperty("data").GetProperty("name").GetString(), Is.EqualTo("Test Product"));
        Assert.That(payload.GetProperty("source").GetString(), Is.EqualTo("component-test"));
    }

    [Test]
    public async Task FetchMessages_ReturnsPublishedMessages()
    {
        // Arrange - Create stream and publish messages directly to NATS
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        // Publish 3 messages directly to NATS
        for (int i = 1; i <= 3; i++)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                message_id = $"msg-{i}",
                timestamp = DateTime.UtcNow.ToString("o"),
                source = "direct-nats",
                data = new { index = i, value = $"test-value-{i}" }
            });
            await JetStream.PublishAsync($"{TestStreamName}.events", payload);
        }

        // Act - Fetch via API
        var response = await Client.GetAsync($"/api/messages/{TestStreamName}.events?limit=10");
        var result = await response.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Count, Is.EqualTo(3));
        Assert.That(result.Messages, Has.Count.EqualTo(3));
        Assert.That(result.Stream, Is.EqualTo(TestStreamName));
    }

    [Test]
    public async Task PublishAndFetch_RoundTrip()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        var testMessages = new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["type"] = "order", ["status"] = "created" },
            new Dictionary<string, object> { ["id"] = 2, ["type"] = "order", ["status"] = "confirmed" },
            new Dictionary<string, object> { ["id"] = 3, ["type"] = "order", ["status"] = "shipped" }
        };

        // Act - Publish multiple messages via API
        foreach (var msg in testMessages)
        {
            var publishRequest = new PublishRequest { Data = msg };
            var pubResponse = await Client.PostAsJsonAsync($"/api/messages/{TestStreamName}.orders", publishRequest);
            Assert.That(pubResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                $"Failed to publish message. Response: {await pubResponse.Content.ReadAsStringAsync()}");
        }

        // Fetch via API
        var fetchResponse = await Client.GetAsync($"/api/messages/{TestStreamName}.orders?limit=10");
        var result = await fetchResponse.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Assert
        Assert.That(fetchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Count, Is.EqualTo(3));

        // Verify message order (should be in publish order)
        var messages = result.Messages;
        Assert.That(messages[0].Sequence, Is.Not.Null);
        Assert.That(messages[1].Sequence, Is.Not.Null);
        Assert.That(messages[2].Sequence, Is.Not.Null);
        Assert.That(messages[0].Sequence!.Value, Is.LessThan(messages[1].Sequence!.Value));
        Assert.That(messages[1].Sequence!.Value, Is.LessThan(messages[2].Sequence!.Value));
    }

    [Test]
    public async Task FetchMessages_WithLimitParameter_ReturnsCorrectCount()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        // Publish 10 messages
        for (int i = 1; i <= 10; i++)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                message_id = $"msg-{i}",
                timestamp = DateTime.UtcNow.ToString("o"),
                source = "test",
                data = new { index = i }
            });
            await JetStream.PublishAsync($"{TestStreamName}.batch", payload);
        }

        // Act - Fetch only 5
        var response = await Client.GetAsync($"/api/messages/{TestStreamName}.batch?limit=5");
        var result = await response.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Count, Is.EqualTo(5));
    }

    [Test]
    public async Task FetchMessages_WithWildcardSubject_ReturnsAllMatching()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        // Publish to different sub-subjects
        await JetStream.PublishAsync($"{TestStreamName}.orders.created",
            JsonSerializer.SerializeToUtf8Bytes(new { type = "created" }));
        await JetStream.PublishAsync($"{TestStreamName}.orders.updated",
            JsonSerializer.SerializeToUtf8Bytes(new { type = "updated" }));
        await JetStream.PublishAsync($"{TestStreamName}.orders.deleted",
            JsonSerializer.SerializeToUtf8Bytes(new { type = "deleted" }));

        // Act - Fetch with wildcard
        var response = await Client.GetAsync($"/api/messages/{TestStreamName}.orders.>?limit=10");
        var result = await response.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task FetchMessages_InvalidLimit_ReturnsBadRequest()
    {
        // Act
        var response = await Client.GetAsync($"/api/messages/{TestStreamName}.test?limit=0");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task FetchMessages_LimitExceedsMax_ReturnsBadRequest()
    {
        // Act
        var response = await Client.GetAsync($"/api/messages/{TestStreamName}.test?limit=101");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task FetchMessages_InvalidTimeout_ReturnsBadRequest()
    {
        // Act
        var response = await Client.GetAsync($"/api/messages/{TestStreamName}.test?timeout=0");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task FetchMessages_TimeoutExceedsMax_ReturnsBadRequest()
    {
        // Act
        var response = await Client.GetAsync($"/api/messages/{TestStreamName}.test?timeout=31");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PublishMessage_WithoutMessageId_GeneratesOne()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        var publishRequest = new PublishRequest
        {
            Data = new { test = true }
            // No MessageId provided
        };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/messages/{TestStreamName}.auto-id", publishRequest);
        var result = await response.Content.ReadFromJsonAsync<PublishResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Published, Is.True);
        Assert.That(result.Sequence, Is.GreaterThan(0));
    }

    [Test]
    public async Task FetchMessages_EmptyStream_ReturnsEmptyList()
    {
        // Arrange - Create stream but don't publish anything
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        // Act
        var response = await Client.GetAsync($"/api/messages/{TestStreamName}.empty?limit=10&timeout=1");
        var result = await response.Content.ReadFromJsonAsync<FetchMessagesResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Count, Is.EqualTo(0));
        Assert.That(result.Messages, Is.Empty);
    }
}
