using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NatsHttpGateway.Controllers;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Controllers;

[TestFixture]
public class HealthControllerTests
{
    private Mock<INatsService> _mockNatsService = null!;
    private HealthController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockNatsService = new Mock<INatsService>();
    }

    [Test]
    public void GetHealth_WhenNatsConnected_ReturnsHealthyStatus()
    {
        // Arrange
        _mockNatsService.Setup(s => s.IsConnected).Returns(true);
        _mockNatsService.Setup(s => s.IsJetStreamAvailable).Returns(true);
        _mockNatsService.Setup(s => s.NatsUrl).Returns("nats://localhost:4222");

        _controller = new HealthController(_mockNatsService.Object);

        // Act
        var result = _controller.GetHealth() as OkObjectResult;
        var healthResponse = result?.Value as HealthResponse;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(healthResponse, Is.Not.Null);
        Assert.That(healthResponse!.Status, Is.EqualTo("healthy"));
        Assert.That(healthResponse.NatsConnected, Is.True);
        Assert.That(healthResponse.JetStreamAvailable, Is.True);
        Assert.That(healthResponse.NatsUrl, Is.EqualTo("nats://localhost:4222"));
    }

    [Test]
    public void GetHealth_WhenNatsDisconnected_ReturnsUnhealthyStatus()
    {
        // Arrange
        _mockNatsService.Setup(s => s.IsConnected).Returns(false);
        _mockNatsService.Setup(s => s.IsJetStreamAvailable).Returns(false);
        _mockNatsService.Setup(s => s.NatsUrl).Returns("nats://localhost:4222");

        _controller = new HealthController(_mockNatsService.Object);

        // Act
        var result = _controller.GetHealth() as OkObjectResult;
        var healthResponse = result?.Value as HealthResponse;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(healthResponse, Is.Not.Null);
        Assert.That(healthResponse!.Status, Is.EqualTo("unhealthy"));
        Assert.That(healthResponse.NatsConnected, Is.False);
    }

    [Test]
    public void GetHealth_IncludesTimestamp()
    {
        // Arrange
        _mockNatsService.Setup(s => s.IsConnected).Returns(true);
        _mockNatsService.Setup(s => s.NatsUrl).Returns("nats://localhost:4222");

        _controller = new HealthController(_mockNatsService.Object);
        var beforeTest = DateTime.UtcNow;

        // Act
        var result = _controller.GetHealth() as OkObjectResult;
        var healthResponse = result?.Value as HealthResponse;
        var afterTest = DateTime.UtcNow;

        // Assert
        Assert.That(healthResponse, Is.Not.Null);
        Assert.That(healthResponse!.Timestamp, Is.GreaterThanOrEqualTo(beforeTest));
        Assert.That(healthResponse.Timestamp, Is.LessThanOrEqualTo(afterTest));
    }
}
