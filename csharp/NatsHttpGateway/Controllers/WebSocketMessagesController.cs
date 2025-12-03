using System.Net.WebSockets;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Mvc;
using NatsHttpGateway.Protos;
using NatsHttpGateway.Services;

namespace NatsHttpGateway.Controllers;

[ApiController]
[Route("ws/[controller]")]
public class WebSocketMessagesController : ControllerBase
{
    private readonly INatsService _natsService;
    private readonly ILogger<WebSocketMessagesController> _logger;

    public WebSocketMessagesController(INatsService natsService, ILogger<WebSocketMessagesController> logger)
    {
        _natsService = natsService;
        _logger = logger;
    }

    /// <summary>
    /// WebSocket endpoint for streaming messages from a subject using an ephemeral consumer
    /// </summary>
    /// <param name="subjectFilter">NATS subject filter (supports wildcards)</param>
    [HttpGet("{subjectFilter}")]
    public async Task StreamMessages(string subjectFilter)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var cts = new CancellationTokenSource();

        try
        {
            _logger.LogInformation("WebSocket connection established for subject filter: {SubjectFilter}", subjectFilter);

            // Send subscription acknowledgment
            await SendControlMessageAsync(webSocket, ControlType.SubscribeAck,
                $"Subscribed to {subjectFilter}", cts.Token);

            // Stream messages from NATS
            await foreach (var message in _natsService.StreamMessagesAsync(subjectFilter, cts.Token))
            {
                try
                {
                    await SendMessageAsync(webSocket, message, string.Empty, cts.Token);
                }
                catch (WebSocketException ex)
                {
                    _logger.LogWarning(ex, "WebSocket send failed for {SubjectFilter}", subjectFilter);
                    break;
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to stream from {SubjectFilter}", subjectFilter);
            await SendControlMessageAsync(webSocket, ControlType.Error, ex.Message, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error streaming from {SubjectFilter}", subjectFilter);
            await SendControlMessageAsync(webSocket, ControlType.Error,
                "Internal server error", cts.Token);
        }
        finally
        {
            await cts.CancelAsync();
            _logger.LogInformation("WebSocket connection closed for {SubjectFilter}", subjectFilter);

            if (webSocket.State == WebSocketState.Open)
            {
                await SendControlMessageAsync(webSocket, ControlType.Close, "Connection closing", CancellationToken.None);
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stream ended", CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// WebSocket endpoint for streaming messages from a durable consumer
    /// </summary>
    /// <param name="stream">NATS stream name</param>
    /// <param name="consumerName">Durable consumer name</param>
    [HttpGet("{stream}/consumer/{consumerName}")]
    public async Task StreamMessagesFromConsumer(string stream, string consumerName)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var cts = new CancellationTokenSource();

        try
        {
            _logger.LogInformation("WebSocket connection established for consumer {ConsumerName} in stream {Stream}",
                consumerName, stream);

            // Send subscription acknowledgment
            await SendControlMessageAsync(webSocket, ControlType.SubscribeAck,
                $"Subscribed to consumer {consumerName}", cts.Token);

            // Stream messages from durable consumer
            await foreach (var message in _natsService.StreamMessagesFromConsumerAsync(stream, consumerName, cts.Token))
            {
                try
                {
                    await SendMessageAsync(webSocket, message, consumerName, cts.Token);
                }
                catch (WebSocketException ex)
                {
                    _logger.LogWarning(ex, "WebSocket send failed for consumer {ConsumerName}", consumerName);
                    break;
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Consumer {ConsumerName} not found in stream {Stream}", consumerName, stream);
            await SendControlMessageAsync(webSocket, ControlType.Error, ex.Message, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error streaming from consumer {ConsumerName}", consumerName);
            await SendControlMessageAsync(webSocket, ControlType.Error,
                "Internal server error", cts.Token);
        }
        finally
        {
            cts.Cancel();
            _logger.LogInformation("WebSocket connection closed for consumer {ConsumerName}", consumerName);

            if (webSocket.State == WebSocketState.Open)
            {
                await SendControlMessageAsync(webSocket, ControlType.Close, "Connection closing", CancellationToken.None);
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stream ended", CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Send a NATS message as a protobuf WebSocket frame
    /// </summary>
    private async Task SendMessageAsync(
        WebSocket webSocket,
        Models.MessageResponse message,
        string consumerName,
        CancellationToken cancellationToken)
    {
        var streamMessage = new StreamMessage
        {
            Subject = message.Subject ?? string.Empty,
            Sequence = message.Sequence ?? 0,
            SizeBytes = message.SizeBytes,
            Consumer = consumerName
        };

        if (message.Timestamp.HasValue)
        {
            streamMessage.Timestamp = Timestamp.FromDateTime(message.Timestamp.Value.ToUniversalTime());
        }

        // Serialize the data field
        if (message.Data != null)
        {
            var dataJson = System.Text.Json.JsonSerializer.Serialize(message.Data);
            streamMessage.Data = Google.Protobuf.ByteString.CopyFromUtf8(dataJson);
        }

        var frame = new WebSocketFrame
        {
            Type = FrameType.Message,
            Message = streamMessage
        };

        var protobufBytes = frame.ToByteArray();
        await webSocket.SendAsync(
            new ArraySegment<byte>(protobufBytes),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken);
    }

    /// <summary>
    /// Send a control message as a protobuf WebSocket frame
    /// </summary>
    private async Task SendControlMessageAsync(
        WebSocket webSocket,
        ControlType type,
        string message,
        CancellationToken cancellationToken)
    {
        var controlMessage = new ControlMessage
        {
            Type = type,
            Message = message
        };

        var frame = new WebSocketFrame
        {
            Type = FrameType.Control,
            Control = controlMessage
        };

        try
        {
            var protobufBytes = frame.ToByteArray();
            await webSocket.SendAsync(
                new ArraySegment<byte>(protobufBytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send control message: {Message}", message);
        }
    }
}
