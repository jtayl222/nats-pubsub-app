using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Component;

/// <summary>
/// Component tests for the /health endpoint with a live NATS connection.
/// These tests verify that the health endpoint accurately reports the real NATS connection state.
/// </summary>
[TestFixture]
[Category("Component")]
public class HealthEndpointComponentTests : NatsComponentTestBase
{
    [Test]
    public async Task Health_WhenNatsConnected_ReturnsHealthyStatus()
    {
        // Act
        var response = await Client.GetAsync("/health");
        var content = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(content, Is.Not.Null);
        Assert.That(content!.Status, Is.EqualTo("healthy"));
        Assert.That(content.NatsConnected, Is.True);
        Assert.That(content.JetStreamAvailable, Is.True);
    }

    [Test]
    public async Task Health_ReturnsCorrectNatsUrl()
    {
        // Act
        var response = await Client.GetAsync("/health");
        var content = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(content, Is.Not.Null);
        Assert.That(content!.NatsUrl, Does.StartWith("nats://"));
        Assert.That(content.NatsUrl, Does.Contain("4222"));
    }

    [Test]
    public async Task Health_IncludesTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var response = await Client.GetAsync("/health");
        var content = await response.Content.ReadFromJsonAsync<HealthResponse>();
        var after = DateTime.UtcNow.AddSeconds(1);

        // Assert
        Assert.That(content, Is.Not.Null);
        Assert.That(content!.Timestamp, Is.GreaterThanOrEqualTo(before));
        Assert.That(content.Timestamp, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public async Task Health_RespondsWithinSLA()
    {
        // Health endpoints should be fast
        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await Client.GetAsync("/health");

        stopwatch.Stop();

        // Assert - should respond within 500ms under normal conditions
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(500),
            $"Health endpoint took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");
    }

    [Test]
    public async Task Health_JetStreamIsAvailable()
    {
        // This test verifies that JetStream is actually available
        // by checking both the health response and making a direct JetStream call

        // Act - Check via API
        var response = await Client.GetAsync("/health");
        var content = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert - API reports JetStream available
        Assert.That(content!.JetStreamAvailable, Is.True);

        // Verify - Confirm by listing streams directly (should not throw)
        var streams = new List<string>();
        await foreach (var stream in JetStream.ListStreamsAsync())
        {
            streams.Add(stream.Info.Config.Name!);
        }

        // If we get here without exception, JetStream is truly available
        Assert.Pass($"JetStream is available. Found {streams.Count} existing streams.");
    }

    /// <summary>
    /// Response model matching the API's HealthResponse (uses snake_case JSON properties)
    /// </summary>
    private class HealthResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("nats_connected")]
        public bool NatsConnected { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nats_url")]
        public string NatsUrl { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("jetstream_available")]
        public bool JetStreamAvailable { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }
}
