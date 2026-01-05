using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NATS.Client.JetStream;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;

namespace NatsHttpGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly INatsService _natsService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(INatsService natsService, ILogger<MessagesController> logger)
    {
        _natsService = natsService;
        _logger = logger;
    }

    /// <summary>
    /// Publish a message to a NATS subject
    /// </summary>
    /// <param name="subject">The NATS subject to publish to</param>
    /// <param name="request">The message payload</param>
    /// <returns>Publication confirmation</returns>
    [HttpPost("{subject}")]
    [ProducesResponseType(typeof(PublishResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PublishMessage(string subject, [FromBody] PublishRequest request)
    {
        try
        {
            _logger.LogInformation("Publishing message to subject: {Subject}", subject);
            var response = await _natsService.PublishAsync(subject, request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to {Subject}", subject);
            return Problem(
                title: "Publish failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Fetch the last N messages from a NATS subject using an ephemeral consumer
    /// </summary>
    /// <param name="subjectFilter">The NATS subject filter (supports wildcards like 'events.>' or 'events.*')</param>
    /// <param name="limit">Maximum number of messages to retrieve (1-100)</param>
    /// <param name="timeout">Timeout in seconds (1-30)</param>
    /// <returns>List of messages</returns>
    [HttpGet("{subjectFilter}")]
    [ProducesResponseType(typeof(FetchMessagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FetchMessages(string subjectFilter, [FromQuery] int limit = 10, [FromQuery] int timeout = 5)
    {
        try
        {
            if (limit < 1 || limit > 100)
            {
                return BadRequest(new { error = "Limit must be between 1 and 100" });
            }

            if (timeout < 1 || timeout > 30)
            {
                return BadRequest(new { error = "Timeout must be between 1 and 30 seconds" });
            }

            _logger.LogInformation("Fetching {Limit} messages from subject filter: {SubjectFilter} with {Timeout}s timeout (ephemeral consumer)",
                limit, subjectFilter, timeout);
            var response = await _natsService.FetchMessagesAsync(subjectFilter, limit, timeout);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages from {SubjectFilter}", subjectFilter);
            return Problem(
                title: "Fetch failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Fetch messages from a NATS stream using a durable (well-known) consumer
    /// </summary>
    /// <param name="stream">The NATS stream name (e.g., 'events', 'payments')</param>
    /// <param name="consumerName">The name of the durable consumer</param>
    /// <param name="limit">Maximum number of messages to retrieve (1-100)</param>
    /// <param name="timeout">Timeout in seconds (1-30)</param>
    /// <returns>List of messages</returns>
    [HttpGet("{stream}/consumer/{consumerName}")]
    [ProducesResponseType(typeof(FetchMessagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FetchMessagesFromConsumer(
        string stream,
        string consumerName,
        [FromQuery] int limit = 10,
        [FromQuery] int timeout = 5)
    {
        try
        {
            if (limit < 1 || limit > 100)
            {
                return BadRequest(new { error = "Limit must be between 1 and 100" });
            }

            if (timeout < 1 || timeout > 30)
            {
                return BadRequest(new { error = "Timeout must be between 1 and 30 seconds" });
            }

            _logger.LogInformation(
                "Fetching {Limit} messages from stream: {Stream} with {Timeout}s timeout (durable consumer: {ConsumerName})",
                limit, stream, timeout, consumerName);
            var response = await _natsService.FetchMessagesFromConsumerAsync(stream, consumerName, limit, timeout);
            return Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("does not exist"))
        {
            _logger.LogWarning(ex, "Consumer {ConsumerName} not found in stream {Stream}", consumerName, stream);
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages from consumer {ConsumerName} in stream {Stream}", consumerName, stream);
            return Problem(
                title: "Fetch from consumer failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}
