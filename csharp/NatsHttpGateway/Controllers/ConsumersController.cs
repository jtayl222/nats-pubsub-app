using Microsoft.AspNetCore.Mvc;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;

namespace NatsHttpGateway.Controllers;

/// <summary>
/// Controller for managing NATS JetStream consumers
/// </summary>
[ApiController]
[Route("api/consumers")]
[Produces("application/json")]
public class ConsumersController : ControllerBase
{
    private readonly INatsService _natsService;
    private readonly ILogger<ConsumersController> _logger;

    public ConsumersController(INatsService natsService, ILogger<ConsumersController> logger)
    {
        _natsService = natsService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new consumer on a stream
    /// </summary>
    /// <param name="stream">Stream name</param>
    /// <param name="request">Consumer configuration</param>
    /// <returns>Created consumer information</returns>
    [HttpPost("{stream}")]
    [ProducesResponseType(typeof(ConsumerDetails), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateConsumer(string stream, [FromBody] CreateConsumerRequest request)
    {
        try
        {
            _logger.LogInformation("Creating consumer {ConsumerName} on stream {StreamName}", request.Name, stream);

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid consumer name",
                    Detail = "Consumer name is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var consumer = await _natsService.CreateConsumerAsync(stream, request);

            return CreatedAtAction(
                nameof(GetConsumer),
                new { stream, consumer = request.Name },
                consumer
            );
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Stream {StreamName} not found", stream);
            return NotFound(new ProblemDetails
            {
                Title = "Stream not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create consumer {ConsumerName} on stream {StreamName}", request.Name, stream);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to create consumer",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get predefined consumer templates
    /// </summary>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(ConsumerTemplatesResponse), StatusCodes.Status200OK)]
    public IActionResult GetConsumerTemplates()
    {
        _logger.LogInformation("Getting consumer templates");
        var result = _natsService.GetConsumerTemplates();
        return Ok(result);
    }

    /// <summary>
    /// List all consumers for a stream
    /// </summary>
    /// <param name="stream">Stream name</param>
    /// <returns>List of consumers</returns>
    [HttpGet("{stream}")]
    [ProducesResponseType(typeof(ConsumerListResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListConsumers(string stream)
    {
        try
        {
            _logger.LogInformation("Listing consumers for stream: {StreamName}", stream);
            var result = await _natsService.ListConsumersAsync(stream);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Stream {StreamName} not found", stream);
            return NotFound(new ProblemDetails
            {
                Title = "Stream not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list consumers for stream {StreamName}", stream);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to list consumers",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get detailed information about a specific consumer
    /// </summary>
    /// <param name="stream">Stream name</param>
    /// <param name="consumer">Consumer name</param>
    /// <returns>Detailed consumer information including metrics</returns>
    [HttpGet("{stream}/{consumer}")]
    [ProducesResponseType(typeof(ConsumerDetails), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetConsumer(string stream, string consumer)
    {
        try
        {
            _logger.LogInformation("Getting info for consumer {ConsumerName} on stream {StreamName}", consumer, stream);
            var result = await _natsService.GetConsumerInfoAsync(stream, consumer);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Consumer {ConsumerName} not found on stream {StreamName}", consumer, stream);
            return NotFound(new ProblemDetails
            {
                Title = "Consumer not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get consumer info for {ConsumerName} on stream {StreamName}", consumer, stream);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to get consumer information",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Delete a consumer from a stream
    /// </summary>
    /// <param name="stream">Stream name</param>
    /// <param name="consumer">Consumer name</param>
    /// <returns>Deletion confirmation</returns>
    [HttpDelete("{stream}/{consumer}")]
    [ProducesResponseType(typeof(ConsumerDeleteResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteConsumer(string stream, string consumer)
    {
        try
        {
            _logger.LogInformation("Deleting consumer {ConsumerName} from stream {StreamName}", consumer, stream);
            var result = await _natsService.DeleteConsumerAsync(stream, consumer);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Consumer {ConsumerName} not found on stream {StreamName}", consumer, stream);
            return NotFound(new ProblemDetails
            {
                Title = "Consumer not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete consumer {ConsumerName} from stream {StreamName}", consumer, stream);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to delete consumer",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Check the health status of a consumer
    /// </summary>
    /// <param name="stream">Stream name</param>
    /// <param name="consumer">Consumer name</param>
    /// <returns>Consumer health information</returns>
    [HttpGet("{stream}/{consumer}/health")]
    [ProducesResponseType(typeof(ConsumerHealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetConsumerHealth(string stream, string consumer)
    {
        try
        {
            _logger.LogInformation("Checking health for consumer {ConsumerName} on stream {StreamName}", consumer, stream);
            var result = await _natsService.GetConsumerHealthAsync(stream, consumer);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Consumer {ConsumerName} not found on stream {StreamName}", consumer, stream);
            return NotFound(new ProblemDetails
            {
                Title = "Consumer not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check health for consumer {ConsumerName} on stream {StreamName}", consumer, stream);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to check consumer health",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Peek at messages from a consumer without acknowledging them
    /// </summary>
    /// <param name="stream">Stream name</param>
    /// <param name="consumer">Consumer name</param>
    /// <param name="limit">Number of messages to peek (default: 10)</param>
    /// <returns>List of message previews</returns>
    [HttpGet("{stream}/{consumer}/messages")]
    [ProducesResponseType(typeof(ConsumerPeekMessagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PeekMessages(string stream, string consumer, [FromQuery] int limit = 10)
    {
        try
        {
            _logger.LogInformation("Peeking {Limit} messages from consumer {ConsumerName} on stream {StreamName}", limit, consumer, stream);
            var result = await _natsService.PeekConsumerMessagesAsync(stream, consumer, limit);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Consumer not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to peek messages from consumer {ConsumerName}", consumer);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to peek messages",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Reset or replay messages from a consumer
    /// </summary>
    /// <param name="stream">Stream name</param>
    /// <param name="consumer">Consumer name</param>
    /// <param name="request">Reset configuration</param>
    /// <returns>Reset confirmation</returns>
    [HttpPost("{stream}/{consumer}/reset")]
    [ProducesResponseType(typeof(ConsumerResetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResetConsumer(string stream, string consumer, [FromBody] ConsumerResetRequest request)
    {
        try
        {
            _logger.LogInformation("Resetting consumer {ConsumerName} on stream {StreamName} with action {Action}",
                consumer, stream, request.Action);
            var result = await _natsService.ResetConsumerAsync(stream, consumer, request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Consumer not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset consumer {ConsumerName}", consumer);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to reset consumer",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Pause a consumer
    /// </summary>
    [HttpPost("{stream}/{consumer}/pause")]
    [ProducesResponseType(typeof(ConsumerActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PauseConsumer(string stream, string consumer)
    {
        try
        {
            _logger.LogInformation("Pausing consumer {ConsumerName} on stream {StreamName}", consumer, stream);
            var result = await _natsService.PauseConsumerAsync(stream, consumer);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Consumer not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause consumer {ConsumerName}", consumer);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to pause consumer",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Resume a consumer
    /// </summary>
    [HttpPost("{stream}/{consumer}/resume")]
    [ProducesResponseType(typeof(ConsumerActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResumeConsumer(string stream, string consumer)
    {
        try
        {
            _logger.LogInformation("Resuming consumer {ConsumerName} on stream {StreamName}", consumer, stream);
            var result = await _natsService.ResumeConsumerAsync(stream, consumer);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Consumer not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume consumer {ConsumerName}", consumer);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to resume consumer",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Create multiple consumers in bulk
    /// </summary>
    [HttpPost("{stream}/bulk-create")]
    [ProducesResponseType(typeof(BulkCreateConsumersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> BulkCreateConsumers(string stream, [FromBody] BulkCreateConsumersRequest request)
    {
        try
        {
            _logger.LogInformation("Bulk creating {Count} consumers on stream {StreamName}", request.Consumers.Count, stream);
            var result = await _natsService.BulkCreateConsumersAsync(stream, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk create consumers on stream {StreamName}", stream);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to bulk create consumers",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get metrics history for a consumer
    /// </summary>
    [HttpGet("{stream}/{consumer}/metrics/history")]
    [ProducesResponseType(typeof(ConsumerMetricsHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetConsumerMetricsHistory(string stream, string consumer, [FromQuery] int samples = 10)
    {
        try
        {
            _logger.LogInformation("Getting metrics history for consumer {ConsumerName}", consumer);
            var result = await _natsService.GetConsumerMetricsHistoryAsync(stream, consumer, samples);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Consumer not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics history for consumer {ConsumerName}", consumer);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to get metrics history",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

}
