using Microsoft.AspNetCore.Mvc;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;

namespace NatsHttpGateway.Controllers;

[ApiController]
[Route("[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly INatsService _natsService;

    public HealthController(INatsService natsService)
    {
        _natsService = natsService;
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    /// <returns>Service health status</returns>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new HealthResponse
        {
            Status = _natsService.IsConnected ? "healthy" : "unhealthy",
            NatsConnected = _natsService.IsConnected,
            NatsUrl = _natsService.NatsUrl,
            JetStreamAvailable = _natsService.IsJetStreamAvailable,
            Timestamp = DateTime.UtcNow
        });
    }
}
