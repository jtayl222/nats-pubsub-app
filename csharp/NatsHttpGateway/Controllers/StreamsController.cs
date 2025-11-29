using Microsoft.AspNetCore.Mvc;
using NATS.Client.JetStream;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;

namespace NatsHttpGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class StreamsController : ControllerBase
{
    private readonly NatsService _natsService;
    private readonly ILogger<StreamsController> _logger;

    public StreamsController(NatsService natsService, ILogger<StreamsController> logger)
    {
        _natsService = natsService;
        _logger = logger;
    }

    /// <summary>
    /// List all JetStream streams
    /// </summary>
    /// <returns>List of all streams with their statistics</returns>
    [HttpGet]
    [ProducesResponseType(typeof(StreamListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListStreams()
    {
        try
        {
            _logger.LogInformation("Listing all JetStream streams");
            var streams = await _natsService.ListStreamsAsync();
            return Ok(new StreamListResponse
            {
                Count = streams.Count,
                Streams = streams
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list streams");
            return Problem(
                title: "Failed to list streams",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Get information about a specific stream
    /// </summary>
    /// <param name="name">The stream name</param>
    /// <returns>Stream information and statistics</returns>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(StreamSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStream(string name)
    {
        try
        {
            _logger.LogInformation("Getting info for stream: {Stream}", name);
            var stream = await _natsService.GetStreamInfoAsync(name);
            return Ok(stream);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            _logger.LogWarning("Stream not found: {Stream}", name);
            return NotFound(new { error = $"Stream '{name}' not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stream info for {Stream}", name);
            return Problem(
                title: "Failed to get stream info",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Get all distinct subjects in a stream
    /// </summary>
    /// <param name="name">The stream name</param>
    /// <returns>List of subjects with message counts</returns>
    [HttpGet("{name}/subjects")]
    [ProducesResponseType(typeof(StreamSubjectsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStreamSubjects(string name)
    {
        try
        {
            _logger.LogInformation("Getting subjects for stream: {Stream}", name);
            var subjects = await _natsService.GetStreamSubjectsAsync(name);
            return Ok(subjects);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            _logger.LogWarning("Stream not found: {Stream}", name);
            return NotFound(new { error = $"Stream '{name}' not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subjects for stream {Stream}", name);
            return Problem(
                title: "Failed to get stream subjects",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}
