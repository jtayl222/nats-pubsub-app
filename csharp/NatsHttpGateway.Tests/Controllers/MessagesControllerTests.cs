using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NATS.Client.JetStream;
using NatsHttpGateway.Controllers;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Controllers;

[TestFixture]
public class MessagesControllerTests
{
    private Mock<INatsService> _mockNatsService = null!;
    private Mock<ILogger<MessagesController>> _mockLogger = null!;
    private MessagesController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockNatsService = new Mock<INatsService>();
        _mockLogger = new Mock<ILogger<MessagesController>>();
        _controller = new MessagesController(_mockNatsService.Object, _mockLogger.Object);
    }

    [Test]
    public async Task PublishMessage_WithValidRequest_ReturnsOkResult()
    {
        // Arrange
        var subject = "test.subject";
        var request = new PublishRequest
        {
            Data = new { message = "test message" }
        };
        var expectedResponse = new PublishResponse
        {
            Published = true,
            Subject = subject,
            Stream = "test",
            Sequence = 1,
            Timestamp = DateTime.UtcNow
        };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, request))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.PublishMessage(subject, request) as OkObjectResult;
        var response = result?.Value as PublishResponse;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Published, Is.True);
        Assert.That(response.Subject, Is.EqualTo(subject));
        Assert.That(response.Stream, Is.EqualTo("test"));
    }

    [Test]
    public async Task PublishMessage_WhenExceptionThrown_ReturnsProblemResult()
    {
        // Arrange
        var subject = "test.subject";
        var request = new PublishRequest { Data = new { } };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, request))
            .ThrowsAsync(new Exception("NATS connection failed"));

        // Act
        var result = await _controller.PublishMessage(subject, request) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task FetchMessages_WithValidLimit_ReturnsOkResult()
    {
        // Arrange
        var subject = "test.subject";
        var limit = 10;
        var expectedResponse = new FetchMessagesResponse
        {
            Subject = subject,
            Count = 2,
            Stream = "test",
            Messages = new List<MessageResponse>
            {
                new() { Subject = subject, Sequence = 1 },
                new() { Subject = subject, Sequence = 2 }
            }
        };

        _mockNatsService
            .Setup(s => s.FetchMessagesAsync(subject, limit, It.IsAny<int>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.FetchMessages(subject, limit) as OkObjectResult;
        var response = result?.Value as FetchMessagesResponse;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Count, Is.EqualTo(2));
        Assert.That(response.Messages, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task FetchMessages_WithLimitBelowMinimum_ReturnsBadRequest()
    {
        // Arrange
        var subject = "test.subject";
        var limit = 0;

        // Act
        var result = await _controller.FetchMessages(subject, limit) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchMessages_WithLimitAboveMaximum_ReturnsBadRequest()
    {
        // Arrange
        var subject = "test.subject";
        var limit = 101;

        // Act
        var result = await _controller.FetchMessages(subject, limit) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchMessages_WhenExceptionThrown_ReturnsProblemResult()
    {
        // Arrange
        var subject = "test.subject";
        var limit = 10;

        _mockNatsService
            .Setup(s => s.FetchMessagesAsync(subject, limit, It.IsAny<int>()))
            .ThrowsAsync(new Exception("Stream not available"));

        // Act
        var result = await _controller.FetchMessages(subject, limit) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }
}
