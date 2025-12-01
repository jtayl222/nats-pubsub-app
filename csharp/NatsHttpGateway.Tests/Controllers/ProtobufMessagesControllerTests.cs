using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NatsHttpGateway.Controllers;
using NatsHttpGateway.Models;
using NatsHttpGateway.Protos;
using NatsHttpGateway.Services;
using NUnit.Framework;
using System.Text;

namespace NatsHttpGateway.Tests.Controllers;

[TestFixture]
public class ProtobufMessagesControllerTests
{
    private Mock<INatsService> _mockNatsService = null!;
    private Mock<ILogger<ProtobufMessagesController>> _mockLogger = null!;
    private ProtobufMessagesController _controller = null!;
    private DefaultHttpContext _httpContext = null!;

    [SetUp]
    public void SetUp()
    {
        _mockNatsService = new Mock<INatsService>();
        _mockLogger = new Mock<ILogger<ProtobufMessagesController>>();
        _controller = new ProtobufMessagesController(_mockNatsService.Object, _mockLogger.Object);

        // Setup HttpContext for the controller
        _httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = _httpContext
        };
    }

    #region PublishProtobufMessage Tests

    [Test]
    public async Task PublishProtobufMessage_WithValidMessage_ReturnsProtobufAck()
    {
        // Arrange
        var subject = "events.test";
        var publishMessage = new PublishMessage
        {
            MessageId = "test-123",
            Subject = subject,
            Source = "unit-test",
            Data = ByteString.CopyFromUtf8("{\"test\":\"data\"}")
        };

        var publishResponse = new PublishResponse
        {
            Published = true,
            Subject = subject,
            Stream = "events",
            Sequence = 1,
            Timestamp = DateTime.UtcNow
        };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, It.IsAny<PublishRequest>()))
            .ReturnsAsync(publishResponse);

        // Create request body with protobuf bytes
        var protobufBytes = publishMessage.ToByteArray();
        _httpContext.Request.Body = new MemoryStream(protobufBytes);
        _httpContext.Request.ContentLength = protobufBytes.Length;

        // Act
        var result = await _controller.PublishProtobufMessage(subject) as FileContentResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentType, Is.EqualTo("application/x-protobuf"));
        Assert.That(result.FileContents.Length, Is.GreaterThan(0));

        // Parse the response
        var ack = PublishAck.Parser.ParseFrom(result.FileContents);
        Assert.That(ack.Published, Is.True);
        Assert.That(ack.Subject, Is.EqualTo(subject));
        Assert.That(ack.Stream, Is.EqualTo("events"));
        Assert.That(ack.Sequence, Is.EqualTo(1UL));
    }

    [Test]
    public async Task PublishProtobufMessage_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var subject = "events.test";
        _httpContext.Request.Body = new MemoryStream(Array.Empty<byte>());
        _httpContext.Request.ContentLength = 0;

        // Act
        var result = await _controller.PublishProtobufMessage(subject) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task PublishProtobufMessage_WithInvalidProtobuf_ReturnsBadRequest()
    {
        // Arrange
        var subject = "events.test";
        var invalidBytes = Encoding.UTF8.GetBytes("not valid protobuf");
        _httpContext.Request.Body = new MemoryStream(invalidBytes);
        _httpContext.Request.ContentLength = invalidBytes.Length;

        // Act
        var result = await _controller.PublishProtobufMessage(subject) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task PublishProtobufMessage_WhenNatsServiceThrows_ReturnsProblem()
    {
        // Arrange
        var subject = "events.test";
        var publishMessage = new PublishMessage
        {
            MessageId = "test-123",
            Data = ByteString.CopyFromUtf8("{\"test\":\"data\"}")
        };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, It.IsAny<PublishRequest>()))
            .ThrowsAsync(new Exception("NATS connection failed"));

        var protobufBytes = publishMessage.ToByteArray();
        _httpContext.Request.Body = new MemoryStream(protobufBytes);
        _httpContext.Request.ContentLength = protobufBytes.Length;

        // Act
        var result = await _controller.PublishProtobufMessage(subject) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    #endregion

    #region FetchProtobufMessages Tests

    [Test]
    public async Task FetchProtobufMessages_WithValidLimit_ReturnsProtobufResponse()
    {
        // Arrange
        var subject = "events.test";
        var limit = 5;

        var fetchResponse = new FetchMessagesResponse
        {
            Subject = subject,
            Count = 2,
            Stream = "events",
            Messages = new List<MessageResponse>
            {
                new() { Subject = subject, Sequence = 1, Data = new { msg = "test1" }, SizeBytes = 50 },
                new() { Subject = subject, Sequence = 2, Data = new { msg = "test2" }, SizeBytes = 51 }
            }
        };

        _mockNatsService
            .Setup(s => s.FetchMessagesAsync(subject, limit, It.IsAny<int>()))
            .ReturnsAsync(fetchResponse);

        // Act
        var result = await _controller.FetchProtobufMessages(subject, limit) as FileContentResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentType, Is.EqualTo("application/x-protobuf"));

        // Parse the response
        var protoResponse = FetchResponse.Parser.ParseFrom(result.FileContents);
        Assert.That(protoResponse.Subject, Is.EqualTo(subject));
        Assert.That(protoResponse.Count, Is.EqualTo(2));
        Assert.That(protoResponse.Stream, Is.EqualTo("events"));
        Assert.That(protoResponse.Messages, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task FetchProtobufMessages_WithLimitBelowMinimum_ReturnsBadRequest()
    {
        // Arrange
        var subject = "events.test";
        var limit = 0;

        // Act
        var result = await _controller.FetchProtobufMessages(subject, limit) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchProtobufMessages_WithLimitAboveMaximum_ReturnsBadRequest()
    {
        // Arrange
        var subject = "events.test";
        var limit = 101;

        // Act
        var result = await _controller.FetchProtobufMessages(subject, limit) as BadRequestObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(400));
        _mockNatsService.Verify(s => s.FetchMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task FetchProtobufMessages_WhenNatsServiceThrows_ReturnsProblem()
    {
        // Arrange
        var subject = "events.test";
        var limit = 10;

        _mockNatsService
            .Setup(s => s.FetchMessagesAsync(subject, limit, It.IsAny<int>()))
            .ThrowsAsync(new Exception("Stream not available"));

        // Act
        var result = await _controller.FetchProtobufMessages(subject, limit) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task FetchProtobufMessages_WithDefaultLimit_Uses10()
    {
        // Arrange
        var subject = "events.test";

        var fetchResponse = new FetchMessagesResponse
        {
            Subject = subject,
            Count = 0,
            Stream = "events",
            Messages = new List<MessageResponse>()
        };

        _mockNatsService
            .Setup(s => s.FetchMessagesAsync(subject, 10, It.IsAny<int>()))
            .ReturnsAsync(fetchResponse);

        // Act
        var result = await _controller.FetchProtobufMessages(subject);

        // Assert
        Assert.That(result, Is.Not.Null);
        _mockNatsService.Verify(s => s.FetchMessagesAsync(subject, 10, It.IsAny<int>()), Times.Once);
    }

    #endregion

    #region PublishUserEvent Tests

    [Test]
    public async Task PublishUserEvent_WithValidEvent_ReturnsProtobufAck()
    {
        // Arrange
        var subject = "events.user.created";
        var userEvent = new UserEvent
        {
            UserId = "user-123",
            EventType = "created",
            Email = "test@example.com",
            OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
        userEvent.Attributes.Add("plan", "premium");

        var publishResponse = new PublishResponse
        {
            Published = true,
            Subject = subject,
            Stream = "events",
            Sequence = 5,
            Timestamp = DateTime.UtcNow
        };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, It.IsAny<PublishRequest>()))
            .ReturnsAsync(publishResponse);

        var protobufBytes = userEvent.ToByteArray();
        _httpContext.Request.Body = new MemoryStream(protobufBytes);
        _httpContext.Request.ContentLength = protobufBytes.Length;

        // Act
        var result = await _controller.PublishUserEvent(subject) as FileContentResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentType, Is.EqualTo("application/x-protobuf"));

        var ack = PublishAck.Parser.ParseFrom(result.FileContents);
        Assert.That(ack.Published, Is.True);
        Assert.That(ack.Stream, Is.EqualTo("events"));
        Assert.That(ack.Sequence, Is.EqualTo(5UL));
    }

    [Test]
    public async Task PublishUserEvent_WhenNatsServiceThrows_ReturnsError()
    {
        // Arrange
        var subject = "events.user.created";
        var userEvent = new UserEvent
        {
            UserId = "user-123",
            EventType = "created"
        };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, It.IsAny<PublishRequest>()))
            .ThrowsAsync(new Exception("Connection lost"));

        var protobufBytes = userEvent.ToByteArray();
        _httpContext.Request.Body = new MemoryStream(protobufBytes);
        _httpContext.Request.ContentLength = protobufBytes.Length;

        // Act
        var result = await _controller.PublishUserEvent(subject) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    #endregion

    #region PublishPaymentEvent Tests

    [Test]
    public async Task PublishPaymentEvent_WithValidEvent_ReturnsProtobufAck()
    {
        // Arrange
        var subject = "payments.credit_card.approved";
        var paymentEvent = new PaymentEvent
        {
            TransactionId = "txn-456",
            Status = "approved",
            Amount = 99.99,
            Currency = "USD",
            CardLastFour = "4242",
            ProcessedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };

        var publishResponse = new PublishResponse
        {
            Published = true,
            Subject = subject,
            Stream = "payments",
            Sequence = 10,
            Timestamp = DateTime.UtcNow
        };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, It.IsAny<PublishRequest>()))
            .ReturnsAsync(publishResponse);

        var protobufBytes = paymentEvent.ToByteArray();
        _httpContext.Request.Body = new MemoryStream(protobufBytes);
        _httpContext.Request.ContentLength = protobufBytes.Length;

        // Act
        var result = await _controller.PublishPaymentEvent(subject) as FileContentResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentType, Is.EqualTo("application/x-protobuf"));

        var ack = PublishAck.Parser.ParseFrom(result.FileContents);
        Assert.That(ack.Published, Is.True);
        Assert.That(ack.Stream, Is.EqualTo("payments"));
        Assert.That(ack.Sequence, Is.EqualTo(10UL));
    }

    [Test]
    public async Task PublishPaymentEvent_WhenNatsServiceThrows_ReturnsError()
    {
        // Arrange
        var subject = "payments.credit_card.approved";
        var paymentEvent = new PaymentEvent
        {
            TransactionId = "txn-456",
            Amount = 99.99,
            Currency = "USD"
        };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, It.IsAny<PublishRequest>()))
            .ThrowsAsync(new Exception("Payment service unavailable"));

        var protobufBytes = paymentEvent.ToByteArray();
        _httpContext.Request.Body = new MemoryStream(protobufBytes);
        _httpContext.Request.ContentLength = protobufBytes.Length;

        // Act
        var result = await _controller.PublishPaymentEvent(subject) as ObjectResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(500));
    }

    #endregion

    #region Content Type Tests

    [Test]
    public async Task PublishProtobufMessage_SetsCorrectContentTypeHeaders()
    {
        // Arrange
        var subject = "events.test";
        var publishMessage = new PublishMessage
        {
            MessageId = "test-123",
            Data = ByteString.CopyFromUtf8("{\"test\":\"data\"}")
        };

        var publishResponse = new PublishResponse
        {
            Published = true,
            Subject = subject,
            Stream = "events",
            Sequence = 1,
            Timestamp = DateTime.UtcNow
        };

        _mockNatsService
            .Setup(s => s.PublishAsync(subject, It.IsAny<PublishRequest>()))
            .ReturnsAsync(publishResponse);

        var protobufBytes = publishMessage.ToByteArray();
        _httpContext.Request.Body = new MemoryStream(protobufBytes);
        _httpContext.Request.ContentLength = protobufBytes.Length;

        // Act
        var result = await _controller.PublishProtobufMessage(subject) as FileContentResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(_httpContext.Response.Headers["Content-Type"].ToString(), Does.Contain("application/x-protobuf"));
        Assert.That(_httpContext.Response.Headers["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
    }

    [Test]
    public async Task FetchProtobufMessages_SetsCorrectContentTypeHeaders()
    {
        // Arrange
        var subject = "events.test";
        var fetchResponse = new FetchMessagesResponse
        {
            Subject = subject,
            Count = 0,
            Stream = "events",
            Messages = new List<MessageResponse>()
        };

        _mockNatsService
            .Setup(s => s.FetchMessagesAsync(subject, 10, It.IsAny<int>()))
            .ReturnsAsync(fetchResponse);

        // Act
        var result = await _controller.FetchProtobufMessages(subject, 10) as FileContentResult;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(_httpContext.Response.Headers["Content-Type"].ToString(), Does.Contain("application/x-protobuf"));
        Assert.That(_httpContext.Response.Headers["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
    }

    #endregion
}
