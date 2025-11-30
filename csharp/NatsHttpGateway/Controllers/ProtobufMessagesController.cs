using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using NatsHttpGateway.Models;
using NatsHttpGateway.Protos;
using NatsHttpGateway.Services;

namespace NatsHttpGateway.Controllers;

[ApiController]
[Route("api/proto/[controller]")]
public class ProtobufMessagesController : ControllerBase
{
    private readonly INatsService _natsService;
    private readonly ILogger<ProtobufMessagesController> _logger;

    public ProtobufMessagesController(INatsService natsService, ILogger<ProtobufMessagesController> logger)
    {
        _natsService = natsService;
        _logger = logger;
    }

    /// <summary>
    /// Helper method to return protobuf bytes without JSON serialization
    /// </summary>
    private FileContentResult ReturnProtobuf(byte[] protobufBytes)
    {
        // Explicitly set response headers to prevent JSON serialization
        Response.Headers["Content-Type"] = "application/x-protobuf";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return new FileContentResult(protobufBytes, "application/x-protobuf")
        {
            FileDownloadName = null // Don't trigger download
        };
    }

    /// <summary>
    /// Publish a message using Protocol Buffers format
    /// </summary>
    /// <param name="subject">The NATS subject to publish to</param>
    /// <returns>Publication confirmation in protobuf format</returns>
    [HttpPost("{subject}")]
    [Consumes("application/x-protobuf")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PublishProtobufMessage(string subject)
    {
        try
        {
            // Read the raw protobuf bytes from request body
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            var protobufBytes = ms.ToArray();

            if (protobufBytes.Length == 0)
            {
                return BadRequest(new { error = "Request body is empty" });
            }

            // Parse the protobuf message
            PublishMessage protoMessage;
            try
            {
                protoMessage = PublishMessage.Parser.ParseFrom(protobufBytes);
            }
            catch (InvalidProtocolBufferException ex)
            {
                _logger.LogWarning(ex, "Failed to parse protobuf message");
                return BadRequest(new { error = "Invalid protobuf format", details = ex.Message });
            }

            _logger.LogInformation("Publishing protobuf message to subject: {Subject}, MessageId: {MessageId}",
                subject, protoMessage.MessageId);

            // Convert protobuf message to internal PublishRequest
            var publishRequest = new PublishRequest
            {
                MessageId = string.IsNullOrEmpty(protoMessage.MessageId) ? null : protoMessage.MessageId,
                Source = string.IsNullOrEmpty(protoMessage.Source) ? "protobuf-gateway" : protoMessage.Source,
                Data = protoMessage.Data.ToByteArray() // Store as raw bytes
            };

            // Publish via NATS service
            var response = await _natsService.PublishAsync(subject, publishRequest);

            // Convert response to protobuf
            var protoAck = new PublishAck
            {
                Published = response.Published,
                Subject = response.Subject,
                Stream = response.Stream,
                Sequence = response.Sequence,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(response.Timestamp.ToUniversalTime())
            };

            // Return protobuf bytes without JSON serialization
            return ReturnProtobuf(protoAck.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish protobuf message to {Subject}", subject);
            return Problem(
                title: "Publish failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Fetch messages in Protocol Buffers format
    /// </summary>
    /// <param name="subject">The NATS subject to fetch from</param>
    /// <param name="limit">Maximum number of messages to retrieve (1-100)</param>
    /// <returns>Messages in protobuf format</returns>
    [HttpGet("{subject}")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FetchProtobufMessages(string subject, [FromQuery] int limit = 10)
    {
        try
        {
            if (limit < 1 || limit > 100)
            {
                return BadRequest(new { error = "Limit must be between 1 and 100" });
            }

            _logger.LogInformation("Fetching {Limit} messages from subject: {Subject} (protobuf)", limit, subject);

            // Fetch messages via NATS service
            var response = await _natsService.FetchMessagesAsync(subject, limit);

            // Convert to protobuf FetchResponse
            var protoResponse = new FetchResponse
            {
                Subject = response.Subject,
                Count = response.Count,
                Stream = response.Stream
            };

            // Convert each message
            foreach (var msg in response.Messages)
            {
                var protoMsg = new FetchedMessage
                {
                    Subject = msg.Subject ?? string.Empty,
                    Sequence = msg.Sequence ?? 0,
                    SizeBytes = msg.SizeBytes,
                    Stream = response.Stream
                };

                if (msg.Timestamp.HasValue)
                {
                    protoMsg.Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
                        msg.Timestamp.Value.ToUniversalTime());
                }

                // Serialize data to bytes
                if (msg.Data != null)
                {
                    var dataJson = System.Text.Json.JsonSerializer.Serialize(msg.Data);
                    protoMsg.Data = ByteString.CopyFromUtf8(dataJson);
                }

                protoResponse.Messages.Add(protoMsg);
            }

            // Return protobuf bytes without JSON serialization
            return ReturnProtobuf(protoResponse.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch protobuf messages from {Subject}", subject);
            return Problem(
                title: "Fetch failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Example: Publish a UserEvent protobuf message
    /// </summary>
    /// <param name="subject">The NATS subject</param>
    /// <returns>Publication confirmation</returns>
    [HttpPost("{subject}/user-event")]
    [Consumes("application/x-protobuf")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishUserEvent(string subject)
    {
        try
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            var userEvent = UserEvent.Parser.ParseFrom(ms.ToArray());

            _logger.LogInformation("Publishing UserEvent: {UserId}, Type: {EventType}",
                userEvent.UserId, userEvent.EventType);

            // Wrap in PublishMessage
            var publishMsg = new PublishRequest
            {
                MessageId = Guid.NewGuid().ToString(),
                Source = "user-service",
                Data = userEvent // Will be serialized as protobuf
            };

            var response = await _natsService.PublishAsync(subject, publishMsg);

            var ack = new PublishAck
            {
                Published = true,
                Subject = subject,
                Stream = response.Stream,
                Sequence = response.Sequence,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            return ReturnProtobuf(ack.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish UserEvent");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Example: Publish a PaymentEvent protobuf message
    /// </summary>
    /// <param name="subject">The NATS subject</param>
    /// <returns>Publication confirmation</returns>
    [HttpPost("{subject}/payment-event")]
    [Consumes("application/x-protobuf")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishPaymentEvent(string subject)
    {
        try
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            var paymentEvent = PaymentEvent.Parser.ParseFrom(ms.ToArray());

            _logger.LogInformation("Publishing PaymentEvent: {TransactionId}, Amount: {Amount} {Currency}",
                paymentEvent.TransactionId, paymentEvent.Amount, paymentEvent.Currency);

            var publishMsg = new PublishRequest
            {
                MessageId = paymentEvent.TransactionId,
                Source = "payment-service",
                Data = paymentEvent
            };

            var response = await _natsService.PublishAsync(subject, publishMsg);

            var ack = new PublishAck
            {
                Published = true,
                Subject = subject,
                Stream = response.Stream,
                Sequence = response.Sequence,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            return ReturnProtobuf(ack.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish PaymentEvent");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
