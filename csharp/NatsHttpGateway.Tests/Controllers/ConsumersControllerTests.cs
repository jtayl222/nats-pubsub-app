using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NatsHttpGateway.Controllers;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Controllers;

[TestFixture]
public class ConsumersControllerTests
{
    private Mock<INatsService> _mockNatsService = null!;
    private Mock<ILogger<ConsumersController>> _mockLogger = null!;
    private ConsumersController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockNatsService = new Mock<INatsService>();
        _mockLogger = new Mock<ILogger<ConsumersController>>();
        _controller = new ConsumersController(_mockNatsService.Object, _mockLogger.Object);
    }

    #region CreateConsumer Tests

    [Test]
    public async Task CreateConsumer_WithValidRequest_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = new CreateConsumerRequest
        {
            Name = "test-consumer",
            Durable = true,
            DeliverPolicy = "all",
            AckPolicy = "explicit"
        };

        var consumerDetails = new ConsumerDetails
        {
            StreamName = "TEST_STREAM",
            Name = "test-consumer",
            Created = DateTime.UtcNow,
            Config = new ConsumerConfiguration(),
            State = new ConsumerStateData(),
            Metrics = new ConsumerMetrics()
        };

        _mockNatsService
            .Setup(s => s.CreateConsumerAsync("TEST_STREAM", request))
            .ReturnsAsync(consumerDetails);

        // Act
        var result = await _controller.CreateConsumer("TEST_STREAM", request);

        // Assert
        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult!.StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        Assert.That(createdResult.Value, Is.EqualTo(consumerDetails));
        Assert.That(createdResult.ActionName, Is.EqualTo(nameof(ConsumersController.GetConsumer)));
    }

    [Test]
    public async Task CreateConsumer_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateConsumerRequest
        {
            Name = "",
            DeliverPolicy = "all"
        };

        // Act
        var result = await _controller.CreateConsumer("TEST_STREAM", request);

        // Assert
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequestResult = result as BadRequestObjectResult;
        var problemDetails = badRequestResult!.Value as ProblemDetails;
        Assert.That(problemDetails, Is.Not.Null);
        Assert.That(problemDetails!.Status, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(problemDetails.Title, Is.EqualTo("Invalid consumer name"));
    }

    [Test]
    public async Task CreateConsumer_WhenStreamNotFound_ReturnsNotFound()
    {
        // Arrange
        var request = new CreateConsumerRequest
        {
            Name = "test-consumer",
            DeliverPolicy = "all"
        };

        _mockNatsService
            .Setup(s => s.CreateConsumerAsync("NONEXISTENT_STREAM", request))
            .ThrowsAsync(new KeyNotFoundException("Stream 'NONEXISTENT_STREAM' not found"));

        // Act
        var result = await _controller.CreateConsumer("NONEXISTENT_STREAM", request);

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        var notFoundResult = result as NotFoundObjectResult;
        var problemDetails = notFoundResult!.Value as ProblemDetails;
        Assert.That(problemDetails!.Status, Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(problemDetails.Title, Is.EqualTo("Stream not found"));
    }

    [Test]
    public async Task CreateConsumer_WhenGeneralError_ReturnsInternalServerError()
    {
        // Arrange
        var request = new CreateConsumerRequest
        {
            Name = "test-consumer",
            DeliverPolicy = "all"
        };

        _mockNatsService
            .Setup(s => s.CreateConsumerAsync("TEST_STREAM", request))
            .ThrowsAsync(new Exception("Something went wrong"));

        // Act
        var result = await _controller.CreateConsumer("TEST_STREAM", request);

        // Assert
        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objectResult = result as ObjectResult;
        Assert.That(objectResult!.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
    }

    #endregion

    #region ListConsumers Tests

    [Test]
    public async Task ListConsumers_WithValidStream_ReturnsConsumerList()
    {
        // Arrange
        var consumerList = new ConsumerListResult
        {
            StreamName = "TEST_STREAM",
            Count = 2,
            Consumers = new List<ConsumerSummary>
            {
                new ConsumerSummary { Name = "consumer1", StreamName = "TEST_STREAM" },
                new ConsumerSummary { Name = "consumer2", StreamName = "TEST_STREAM" }
            }
        };

        _mockNatsService
            .Setup(s => s.ListConsumersAsync("TEST_STREAM"))
            .ReturnsAsync(consumerList);

        // Act
        var result = await _controller.ListConsumers("TEST_STREAM");

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult!.Value, Is.EqualTo(consumerList));
    }

    [Test]
    public async Task ListConsumers_WhenStreamNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockNatsService
            .Setup(s => s.ListConsumersAsync("NONEXISTENT"))
            .ThrowsAsync(new KeyNotFoundException("Stream not found"));

        // Act
        var result = await _controller.ListConsumers("NONEXISTENT");

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    #endregion

    #region GetConsumer Tests

    [Test]
    public async Task GetConsumer_WithValidConsumer_ReturnsConsumerDetails()
    {
        // Arrange
        var consumerDetails = new ConsumerDetails
        {
            StreamName = "TEST_STREAM",
            Name = "test-consumer",
            Created = DateTime.UtcNow,
            Metrics = new ConsumerMetrics
            {
                ConsumerLag = 10,
                PendingMessages = 5,
                IsHealthy = true
            }
        };

        _mockNatsService
            .Setup(s => s.GetConsumerInfoAsync("TEST_STREAM", "test-consumer"))
            .ReturnsAsync(consumerDetails);

        // Act
        var result = await _controller.GetConsumer("TEST_STREAM", "test-consumer");

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedDetails = okResult!.Value as ConsumerDetails;
        Assert.That(returnedDetails, Is.Not.Null);
        Assert.That(returnedDetails!.Name, Is.EqualTo("test-consumer"));
        Assert.That(returnedDetails.Metrics.ConsumerLag, Is.EqualTo(10));
    }

    [Test]
    public async Task GetConsumer_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockNatsService
            .Setup(s => s.GetConsumerInfoAsync("TEST_STREAM", "nonexistent"))
            .ThrowsAsync(new KeyNotFoundException("Consumer not found"));

        // Act
        var result = await _controller.GetConsumer("TEST_STREAM", "nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    #endregion

    #region DeleteConsumer Tests

    [Test]
    public async Task DeleteConsumer_WithValidConsumer_ReturnsOk()
    {
        // Arrange
        var deleteResult = new ConsumerDeleteResult
        {
            Success = true,
            Message = "Consumer deleted successfully"
        };

        _mockNatsService
            .Setup(s => s.DeleteConsumerAsync("TEST_STREAM", "test-consumer"))
            .ReturnsAsync(deleteResult);

        // Act
        var result = await _controller.DeleteConsumer("TEST_STREAM", "test-consumer");

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResult = okResult!.Value as ConsumerDeleteResult;
        Assert.That(returnedResult!.Success, Is.True);
    }

    [Test]
    public async Task DeleteConsumer_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockNatsService
            .Setup(s => s.DeleteConsumerAsync("TEST_STREAM", "nonexistent"))
            .ThrowsAsync(new KeyNotFoundException("Consumer not found"));

        // Act
        var result = await _controller.DeleteConsumer("TEST_STREAM", "nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    #endregion

    #region GetConsumerHealth Tests

    [Test]
    public async Task GetConsumerHealth_WithHealthyConsumer_ReturnsHealthyStatus()
    {
        // Arrange
        var healthResponse = new ConsumerHealthResponse
        {
            ConsumerName = "test-consumer",
            StreamName = "TEST_STREAM",
            IsHealthy = true,
            Status = "Healthy",
            LastActivity = DateTime.UtcNow,
            PendingMessages = 5,
            AckPending = 2
        };

        _mockNatsService
            .Setup(s => s.GetConsumerHealthAsync("TEST_STREAM", "test-consumer"))
            .ReturnsAsync(healthResponse);

        // Act
        var result = await _controller.GetConsumerHealth("TEST_STREAM", "test-consumer");

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedHealth = okResult!.Value as ConsumerHealthResponse;
        Assert.That(returnedHealth!.IsHealthy, Is.True);
        Assert.That(returnedHealth.Status, Is.EqualTo("Healthy"));
    }

    [Test]
    public async Task GetConsumerHealth_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockNatsService
            .Setup(s => s.GetConsumerHealthAsync("TEST_STREAM", "nonexistent"))
            .ThrowsAsync(new KeyNotFoundException("Consumer not found"));

        // Act
        var result = await _controller.GetConsumerHealth("TEST_STREAM", "nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    #endregion

    #region PeekMessages Tests

    [Test]
    public async Task PeekMessages_WithValidRequest_ReturnsMessages()
    {
        // Arrange
        var peekResponse = new ConsumerPeekMessagesResponse
        {
            ConsumerName = "test-consumer",
            StreamName = "TEST_STREAM",
            Count = 3,
            Messages = new List<MessagePreview>
            {
                new MessagePreview { Sequence = 1, Subject = "test.subject", DataPreview = "message 1" },
                new MessagePreview { Sequence = 2, Subject = "test.subject", DataPreview = "message 2" },
                new MessagePreview { Sequence = 3, Subject = "test.subject", DataPreview = "message 3" }
            }
        };

        _mockNatsService
            .Setup(s => s.PeekConsumerMessagesAsync("TEST_STREAM", "test-consumer", 10))
            .ReturnsAsync(peekResponse);

        // Act
        var result = await _controller.PeekMessages("TEST_STREAM", "test-consumer", 10);

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as ConsumerPeekMessagesResponse;
        Assert.That(returnedResponse!.Count, Is.EqualTo(3));
        Assert.That(returnedResponse.Messages.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task PeekMessages_WithDefaultLimit_Uses10AsDefault()
    {
        // Arrange
        var peekResponse = new ConsumerPeekMessagesResponse
        {
            ConsumerName = "test-consumer",
            StreamName = "TEST_STREAM",
            Count = 0,
            Messages = new List<MessagePreview>()
        };

        _mockNatsService
            .Setup(s => s.PeekConsumerMessagesAsync("TEST_STREAM", "test-consumer", 10))
            .ReturnsAsync(peekResponse);

        // Act
        var result = await _controller.PeekMessages("TEST_STREAM", "test-consumer");

        // Assert
        _mockNatsService.Verify(s => s.PeekConsumerMessagesAsync("TEST_STREAM", "test-consumer", 10), Times.Once);
    }

    #endregion

    #region ResetConsumer Tests

    [Test]
    public async Task ResetConsumer_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new ConsumerResetRequest
        {
            Action = "reset"
        };

        var resetResponse = new ConsumerResetResponse
        {
            Success = true,
            Message = "Consumer reset successfully",
            ConsumerName = "test-consumer"
        };

        _mockNatsService
            .Setup(s => s.ResetConsumerAsync("TEST_STREAM", "test-consumer", request))
            .ReturnsAsync(resetResponse);

        // Act
        var result = await _controller.ResetConsumer("TEST_STREAM", "test-consumer", request);

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as ConsumerResetResponse;
        Assert.That(returnedResponse!.Success, Is.True);
    }

    #endregion

    #region PauseConsumer Tests

    [Test]
    public async Task PauseConsumer_WithValidConsumer_ReturnsSuccess()
    {
        // Arrange
        var actionResponse = new ConsumerActionResponse
        {
            Success = true,
            Action = "pause",
            ConsumerName = "test-consumer",
            Message = "Consumer paused (note: NATS doesn't support native pause)"
        };

        _mockNatsService
            .Setup(s => s.PauseConsumerAsync("TEST_STREAM", "test-consumer"))
            .ReturnsAsync(actionResponse);

        // Act
        var result = await _controller.PauseConsumer("TEST_STREAM", "test-consumer");

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as ConsumerActionResponse;
        Assert.That(returnedResponse!.Action, Is.EqualTo("pause"));
    }

    #endregion

    #region ResumeConsumer Tests

    [Test]
    public async Task ResumeConsumer_WithValidConsumer_ReturnsSuccess()
    {
        // Arrange
        var actionResponse = new ConsumerActionResponse
        {
            Success = true,
            Action = "resume",
            ConsumerName = "test-consumer",
            Message = "Consumers are always active in NATS"
        };

        _mockNatsService
            .Setup(s => s.ResumeConsumerAsync("TEST_STREAM", "test-consumer"))
            .ReturnsAsync(actionResponse);

        // Act
        var result = await _controller.ResumeConsumer("TEST_STREAM", "test-consumer");

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as ConsumerActionResponse;
        Assert.That(returnedResponse!.Action, Is.EqualTo("resume"));
    }

    #endregion

    #region BulkCreateConsumers Tests

    [Test]
    public async Task BulkCreateConsumers_WithValidRequest_ReturnsResults()
    {
        // Arrange
        var request = new BulkCreateConsumersRequest
        {
            Consumers = new List<CreateConsumerRequest>
            {
                new CreateConsumerRequest { Name = "consumer1", DeliverPolicy = "all" },
                new CreateConsumerRequest { Name = "consumer2", DeliverPolicy = "new" }
            }
        };

        var bulkResponse = new BulkCreateConsumersResponse
        {
            TotalRequested = 2,
            SuccessCount = 2,
            FailureCount = 0,
            Results = new List<ConsumerCreateResult>
            {
                new ConsumerCreateResult { Name = "consumer1", Success = true },
                new ConsumerCreateResult { Name = "consumer2", Success = true }
            }
        };

        _mockNatsService
            .Setup(s => s.BulkCreateConsumersAsync("TEST_STREAM", request))
            .ReturnsAsync(bulkResponse);

        // Act
        var result = await _controller.BulkCreateConsumers("TEST_STREAM", request);

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as BulkCreateConsumersResponse;
        Assert.That(returnedResponse!.SuccessCount, Is.EqualTo(2));
        Assert.That(returnedResponse.FailureCount, Is.EqualTo(0));
    }

    [Test]
    public async Task BulkCreateConsumers_WithPartialFailure_ReturnsAllResults()
    {
        // Arrange
        var request = new BulkCreateConsumersRequest
        {
            Consumers = new List<CreateConsumerRequest>
            {
                new CreateConsumerRequest { Name = "consumer1", DeliverPolicy = "all" },
                new CreateConsumerRequest { Name = "consumer2", DeliverPolicy = "new" }
            }
        };

        var bulkResponse = new BulkCreateConsumersResponse
        {
            TotalRequested = 2,
            SuccessCount = 1,
            FailureCount = 1,
            Results = new List<ConsumerCreateResult>
            {
                new ConsumerCreateResult { Name = "consumer1", Success = true },
                new ConsumerCreateResult { Name = "consumer2", Success = false, Error = "Name already exists" }
            }
        };

        _mockNatsService
            .Setup(s => s.BulkCreateConsumersAsync("TEST_STREAM", request))
            .ReturnsAsync(bulkResponse);

        // Act
        var result = await _controller.BulkCreateConsumers("TEST_STREAM", request);

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as BulkCreateConsumersResponse;
        Assert.That(returnedResponse!.SuccessCount, Is.EqualTo(1));
        Assert.That(returnedResponse.FailureCount, Is.EqualTo(1));
    }

    #endregion

    #region GetConsumerMetricsHistory Tests

    [Test]
    public async Task GetConsumerMetricsHistory_WithValidRequest_ReturnsHistory()
    {
        // Arrange
        var historyResponse = new ConsumerMetricsHistoryResponse
        {
            ConsumerName = "test-consumer",
            StreamName = "TEST_STREAM",
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            EndTime = DateTime.UtcNow,
            Count = 3,
            History = new List<ConsumerMetricsSnapshot>
            {
                new ConsumerMetricsSnapshot { Timestamp = DateTime.UtcNow.AddMinutes(-10), ConsumerLag = 100 },
                new ConsumerMetricsSnapshot { Timestamp = DateTime.UtcNow.AddMinutes(-5), ConsumerLag = 50 },
                new ConsumerMetricsSnapshot { Timestamp = DateTime.UtcNow, ConsumerLag = 10 }
            }
        };

        _mockNatsService
            .Setup(s => s.GetConsumerMetricsHistoryAsync("TEST_STREAM", "test-consumer", 10))
            .ReturnsAsync(historyResponse);

        // Act
        var result = await _controller.GetConsumerMetricsHistory("TEST_STREAM", "test-consumer", 10);

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as ConsumerMetricsHistoryResponse;
        Assert.That(returnedResponse!.History.Count, Is.EqualTo(3));
        Assert.That(returnedResponse.History[0].ConsumerLag, Is.EqualTo(100));
        Assert.That(returnedResponse.History[2].ConsumerLag, Is.EqualTo(10));
    }

    #endregion

    #region GetConsumerTemplates Tests

    [Test]
    public void GetConsumerTemplates_ReturnsTemplates()
    {
        // Arrange
        var templatesResponse = new ConsumerTemplatesResponse
        {
            Count = 6,
            Templates = new List<ConsumerTemplate>
            {
                new ConsumerTemplate
                {
                    Name = "real-time-processor",
                    Description = "Processes new messages in real-time",
                    UseCase = "Event processing",
                    Template = new CreateConsumerRequest { Durable = false, DeliverPolicy = "new" }
                },
                new ConsumerTemplate
                {
                    Name = "batch-processor",
                    Description = "Processes all messages",
                    UseCase = "Batch processing",
                    Template = new CreateConsumerRequest { Durable = true, DeliverPolicy = "all" }
                }
            }
        };

        _mockNatsService
            .Setup(s => s.GetConsumerTemplates())
            .Returns(templatesResponse);

        // Act
        var result = _controller.GetConsumerTemplates();

        // Assert
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as ConsumerTemplatesResponse;
        Assert.That(returnedResponse!.Count, Is.GreaterThan(0));
        Assert.That(returnedResponse.Templates.Count, Is.GreaterThan(0));
    }

    [Test]
    public void GetConsumerTemplates_TemplatesIncludeDurabilitySettings()
    {
        // Arrange
        var templatesResponse = new ConsumerTemplatesResponse
        {
            Count = 2,
            Templates = new List<ConsumerTemplate>
            {
                new ConsumerTemplate
                {
                    Name = "ephemeral-template",
                    Template = new CreateConsumerRequest { Durable = false }
                },
                new ConsumerTemplate
                {
                    Name = "durable-template",
                    Template = new CreateConsumerRequest { Durable = true }
                }
            }
        };

        _mockNatsService
            .Setup(s => s.GetConsumerTemplates())
            .Returns(templatesResponse);

        // Act
        var result = _controller.GetConsumerTemplates();

        // Assert
        var okResult = result as OkObjectResult;
        var returnedResponse = okResult!.Value as ConsumerTemplatesResponse;

        var ephemeralTemplate = returnedResponse!.Templates.First(t => t.Name == "ephemeral-template");
        var durableTemplate = returnedResponse.Templates.First(t => t.Name == "durable-template");

        Assert.That(ephemeralTemplate.Template.Durable, Is.False);
        Assert.That(durableTemplate.Template.Durable, Is.True);
    }

    #endregion
}
