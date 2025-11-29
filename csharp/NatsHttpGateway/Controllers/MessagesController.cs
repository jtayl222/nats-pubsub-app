using Microsoft.AspNetCore.Mvc;
using NATS.Client.JetStream;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;

namespace NatsHttpGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
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
    /// Fetch the last N messages from a NATS subject
    /// </summary>
    /// <param name="subject">The NATS subject to fetch from</param>
    /// <param name="limit">Maximum number of messages to retrieve (1-100)</param>
    /// <returns>List of messages</returns>
    [HttpGet("{subject}")]
    [ProducesResponseType(typeof(FetchMessagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FetchMessages(string subject, [FromQuery] int limit = 10)
    {
        try
        {
            if (limit < 1 || limit > 100)
            {
                return BadRequest(new { error = "Limit must be between 1 and 100" });
            }

            _logger.LogInformation("Fetching {Limit} messages from subject: {Subject}", limit, subject);
            var response = await _natsService.FetchMessagesAsync(subject, limit);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages from {Subject}", subject);
            return Problem(
                title: "Fetch failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}
