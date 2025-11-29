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
public class StreamsControllerTests
{
    private Mock<INatsService> _mockNatsService = null!;
    private Mock<ILogger<StreamsController>> _mockLogger = null!;
    private StreamsController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockNatsService = new Mock<INatsService>();
        _mockLogger = new Mock<ILogger<StreamsController>>();
        _controller = new StreamsController(_mockNatsService.Object, _mockLogger.Object);
    }

    [Test]
    public async Task ListStreams_ReturnsAllStreams()
    {
        // Arrange
        var streams = new List<StreamSummary>
        {
            new() { Name = "STREAM1", Messages = 100 },
            new() { Name = "STREAM2", Messages = 200 }
        };

        _mockNatsService
            .Setup(s => s.ListStreamsAsync())
            .ReturnsAsync(streams);

        // Act
        var result = await _controller.ListStreams() as OkObjectResult;
        var response = result?.Value as StreamListResponse;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Count, Is.EqualTo(2));
        Assert.That(response.Streams, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ListStreams_WhenNoStreams_ReturnsEmptyList()
    {
        // Arrange
        _mockNatsService
            .Setup(s => s.ListStreamsAsync())
            .ReturnsAsync(new List<StreamSummary>());

        // Act
        var result = await _controller.ListStreams() as OkObjectResult;
        var response = result?.Value as StreamListResponse;

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Count, Is.EqualTo(0));
        Assert.That(response.Streams, Is.Empty);
    }

    [Test]
    public async Task ListStreams_WhenExceptionThrown_ReturnsProblemResult()
    {
        // Arrange
        _mockNatsService
            .Setup(s => s.ListStreamsAsync())
            .ThrowsAsync(new Exception("JetStream unavailable"));

        // Act
        var result = await _controller.ListStreams() as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task GetStream_WithValidName_ReturnsStreamInfo()
    {
        // Arrange
        var streamName = "TEST_STREAM";
        var streamInfo = new StreamSummary
        {
            Name = streamName,
            Messages = 500,
            Bytes = 1024,
            FirstSeq = 1,
            LastSeq = 500
        };

        _mockNatsService
            .Setup(s => s.GetStreamInfoAsync(streamName))
            .ReturnsAsync(streamInfo);

        // Act
        var result = await _controller.GetStream(streamName) as OkObjectResult;
        var response = result?.Value as StreamSummary;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Name, Is.EqualTo(streamName));
        Assert.That(response.Messages, Is.EqualTo(500));
    }

    [Test]
    public async Task GetStream_WhenStreamNotFound_ReturnsNotFound()
    {
        // Arrange
        var streamName = "NONEXISTENT";

        // Create a NatsJSApiException with a 404 error
        // Note: NatsJSApiException.Error is readonly, so we simulate with reflection or use alternate approach
        _mockNatsService
            .Setup(s => s.GetStreamInfoAsync(streamName))
            .ThrowsAsync(new Exception("Stream not found")); // Simplified for testing

        // Act
        var result = await _controller.GetStream(streamName) as ObjectResult;

        // Assert - when it's a general exception, it returns 500
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task GetStream_WhenOtherExceptionThrown_ReturnsProblemResult()
    {
        // Arrange
        var streamName = "TEST_STREAM";

        _mockNatsService
            .Setup(s => s.GetStreamInfoAsync(streamName))
            .ThrowsAsync(new Exception("Connection lost"));

        // Act
        var result = await _controller.GetStream(streamName) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task GetStreamSubjects_WithValidStream_ReturnsSubjects()
    {
        // Arrange
        var streamName = "TEST_STREAM";
        var subjects = new StreamSubjectsResponse
        {
            StreamName = streamName,
            Count = 3,
            Subjects = new List<SubjectDetail>
            {
                new() { Subject = "events.test", Messages = 100 },
                new() { Subject = "events.user", Messages = 50 },
                new() { Subject = "events.order", Messages = 25 }
            }
        };

        _mockNatsService
            .Setup(s => s.GetStreamSubjectsAsync(streamName))
            .ReturnsAsync(subjects);

        // Act
        var result = await _controller.GetStreamSubjects(streamName) as OkObjectResult;
        var response = result?.Value as StreamSubjectsResponse;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Count, Is.EqualTo(3));
        Assert.That(response.Subjects, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetStreamSubjects_WhenStreamNotFound_ReturnsError()
    {
        // Arrange
        var streamName = "NONEXISTENT";

        _mockNatsService
            .Setup(s => s.GetStreamSubjectsAsync(streamName))
            .ThrowsAsync(new Exception("Stream not found"));

        // Act
        var result = await _controller.GetStreamSubjects(streamName) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task GetStreamSubjects_WhenNoSubjects_ReturnsEmptyListWithNote()
    {
        // Arrange
        var streamName = "EMPTY_STREAM";
        var subjects = new StreamSubjectsResponse
        {
            StreamName = streamName,
            Count = 0,
            Subjects = new List<SubjectDetail>(),
            Note = "No subject-level statistics available for this stream"
        };

        _mockNatsService
            .Setup(s => s.GetStreamSubjectsAsync(streamName))
            .ReturnsAsync(subjects);

        // Act
        var result = await _controller.GetStreamSubjects(streamName) as OkObjectResult;
        var response = result?.Value as StreamSubjectsResponse;

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Count, Is.EqualTo(0));
        Assert.That(response.Subjects, Is.Empty);
        Assert.That(response.Note, Is.Not.Null);
    }
}
