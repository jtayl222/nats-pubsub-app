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

    [Test]
    public async Task FetchMessages_WithTimeoutBelowMinimum_ReturnsBadRequest()
    {
        // Arrange
        var subject = "test.subject";
        var limit = 10;
        var timeout = 0;

        // Act
        var result = await _controller.FetchMessages(subject, limit, timeout) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchMessages_WithTimeoutAboveMaximum_ReturnsBadRequest()
    {
        // Arrange
        var subject = "test.subject";
        var limit = 10;
        var timeout = 31;

        // Act
        var result = await _controller.FetchMessages(subject, limit, timeout) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchMessagesFromConsumer_WithValidParameters_ReturnsOkResult()
    {
        // Arrange
        var stream = "events";
        var consumerName = "my-consumer";
        var limit = 10;
        var timeout = 5;
        var expectedResponse = new FetchMessagesResponse
        {
            Subject = string.Empty,
            Count = 3,
            Stream = stream,
            Messages = new List<MessageResponse>
            {
                new() { Subject = "events.test", Sequence = 1 },
                new() { Subject = "events.test", Sequence = 2 },
                new() { Subject = "events.test", Sequence = 3 }
            }
        };

        _mockNatsService
            .Setup(s => s.FetchMessagesFromConsumerAsync(stream, consumerName, limit, timeout))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.FetchMessagesFromConsumer(stream, consumerName, limit, timeout) as OkObjectResult;
        var response = result?.Value as FetchMessagesResponse;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Count, Is.EqualTo(3));
        Assert.That(response.Stream, Is.EqualTo(stream));
        Assert.That(response.Messages, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task FetchMessagesFromConsumer_WhenConsumerNotFound_ReturnsNotFound()
    {
        // Arrange
        var stream = "events";
        var consumerName = "nonexistent-consumer";
        var limit = 10;

        _mockNatsService
            .Setup(s => s.FetchMessagesFromConsumerAsync(stream, consumerName, limit, It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException(
                $"Consumer '{consumerName}' does not exist in stream '{stream}'. " +
                "Please create the consumer first using the NATS CLI or management API."));

        // Act
        var result = await _controller.FetchMessagesFromConsumer(stream, consumerName, limit) as NotFoundObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(404));
        var errorResponse = result.Value as dynamic;
        Assert.That(errorResponse, Is.Not.Null);
    }

    [Test]
    public async Task FetchMessagesFromConsumer_WithLimitBelowMinimum_ReturnsBadRequest()
    {
        // Arrange
        var stream = "events";
        var consumerName = "my-consumer";
        var limit = 0;

        // Act
        var result = await _controller.FetchMessagesFromConsumer(stream, consumerName, limit) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesFromConsumerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchMessagesFromConsumer_WithLimitAboveMaximum_ReturnsBadRequest()
    {
        // Arrange
        var stream = "events";
        var consumerName = "my-consumer";
        var limit = 101;

        // Act
        var result = await _controller.FetchMessagesFromConsumer(stream, consumerName, limit) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesFromConsumerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchMessagesFromConsumer_WithTimeoutBelowMinimum_ReturnsBadRequest()
    {
        // Arrange
        var stream = "events";
        var consumerName = "my-consumer";
        var limit = 10;
        var timeout = 0;

        // Act
        var result = await _controller.FetchMessagesFromConsumer(stream, consumerName, limit, timeout) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesFromConsumerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchMessagesFromConsumer_WithTimeoutAboveMaximum_ReturnsBadRequest()
    {
        // Arrange
        var stream = "events";
        var consumerName = "my-consumer";
        var limit = 10;
        var timeout = 31;

        // Act
        var result = await _controller.FetchMessagesFromConsumer(stream, consumerName, limit, timeout) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesFromConsumerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchMessagesFromConsumer_WhenExceptionThrown_ReturnsProblemResult()
    {
        // Arrange
        var stream = "events";
        var consumerName = "my-consumer";
        var limit = 10;

        _mockNatsService
            .Setup(s => s.FetchMessagesFromConsumerAsync(stream, consumerName, limit, It.IsAny<int>()))
            .ThrowsAsync(new Exception("NATS connection lost"));

        // Act
        var result = await _controller.FetchMessagesFromConsumer(stream, consumerName, limit) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }
}
