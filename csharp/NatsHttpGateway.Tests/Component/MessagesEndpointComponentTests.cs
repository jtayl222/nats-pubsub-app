using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.JetStream.Models;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Component;

/// <summary>
/// Component tests for the /api/messages endpoints with a live NATS connection.
/// These tests verify publish and fetch operations against real NATS JetStream.
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
        Assert.That(messages[0].Sequence, Is.LessThan(messages[1].Sequence));
        Assert.That(messages[1].Sequence, Is.LessThan(messages[2].Sequence));
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
    public async Task VerifyWithNatsBox_PublishedMessageVisible()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        var uniqueValue = Guid.NewGuid().ToString();
        var publishRequest = new PublishRequest
        {
            MessageId = "natsbox-test",
            Data = new Dictionary<string, object>
            {
                ["uniqueMarker"] = uniqueValue,
                ["testName"] = "nats-box-verification"
            }
        };

        // Act - Publish via API
        var response = await Client.PostAsJsonAsync($"/api/messages/{TestStreamName}.natsbox", publishRequest);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"Failed to publish. Response: {await response.Content.ReadAsStringAsync()}");

        // Verify with nats-box - get the last message for this subject
        var natsBoxOutput = await RunNatsBoxCommandAsync($"stream get {TestStreamName} -S {TestStreamName}.natsbox");

        // Assert - nats-box should see the message
        Assert.That(natsBoxOutput, Does.Contain(uniqueValue),
            $"nats-box output should contain the unique marker. Output was:\n{natsBoxOutput}");
        Assert.That(natsBoxOutput, Does.Contain("natsbox-test").Or.Contain("nats-box-verification"),
            "nats-box output should contain message identifiers");
    }

    [Test]
    public async Task VerifyWithNatsBox_StreamExists()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        // Publish a message to ensure stream has data
        var publishRequest = new PublishRequest { Data = new { test = true } };
        await Client.PostAsJsonAsync($"/api/messages/{TestStreamName}.init", publishRequest);

        // Act - List streams with nats-box
        var natsBoxOutput = await RunNatsBoxCommandAsync("stream list");

        // Assert
        Assert.That(natsBoxOutput, Does.Contain(TestStreamName),
            $"Stream {TestStreamName} should be visible in nats-box stream list");
    }

    [Test]
    public async Task VerifyWithNatsBox_StreamInfo()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        // Publish multiple messages
        for (int i = 0; i < 5; i++)
        {
            var publishRequest = new PublishRequest { Data = new { messageNumber = i } };
            await Client.PostAsJsonAsync($"/api/messages/{TestStreamName}.info", publishRequest);
        }

        // Act - Get stream info with nats-box
        var natsBoxOutput = await RunNatsBoxCommandAsync($"stream info {TestStreamName}");

        // Assert
        Assert.That(natsBoxOutput, Does.Contain("Messages:").Or.Contain("messages"),
            "Stream info should show message count");
        Assert.That(natsBoxOutput, Does.Contain(TestStreamName),
            "Stream info should contain stream name");
    }

    /// <summary>
    /// Runs a nats CLI command using nats-box Docker container
    /// </summary>
    private async Task<string> RunNatsBoxCommandAsync(string command)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"run --rm --network host natsio/nats-box nats -s nats://host.docker.internal:4222 {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        // Return combined output, preferring stdout but including stderr if needed
        if (!string.IsNullOrWhiteSpace(output))
            return output;
        if (!string.IsNullOrWhiteSpace(error))
            return error;
        return $"No output. Exit code: {process.ExitCode}";
    }

    #region Response Models

    private class PublishRequest
    {
        [JsonPropertyName("message_id")]
        public string? MessageId { get; set; }

        [JsonPropertyName("data")]
        public object? Data { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; } = "component-test";
    }

    private class PublishResponse
    {
        [JsonPropertyName("published")]
        public bool Published { get; set; }

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public string Stream { get; set; } = string.Empty;

        [JsonPropertyName("sequence")]
        public ulong Sequence { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    private class FetchMessagesResponse
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("messages")]
        public List<MessageResponse> Messages { get; set; } = new();

        [JsonPropertyName("stream")]
        public string? Stream { get; set; }
    }

    private class MessageResponse
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("sequence")]
        public ulong? Sequence { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonPropertyName("data")]
        public object? Data { get; set; }

        [JsonPropertyName("size_bytes")]
        public int SizeBytes { get; set; }
    }

    #endregion
}
