using NatsHttpGateway.Protos;
using Google.Protobuf;
using System.Net.WebSockets;
using System.Text;

namespace NatsHttpGateway.Examples;

/// <summary>
/// Example client demonstrating how to use WebSocket streaming endpoints
/// </summary>
public class WebSocketClientExample
{
    private readonly string _baseUrl;
    private readonly string _wsBaseUrl;

    public WebSocketClientExample(string? baseUrl = null)
    {
        // Configuration priority: Parameter > Environment variable > Default
        _baseUrl = baseUrl
            ?? Environment.GetEnvironmentVariable("NATS_GATEWAY_URL")
            ?? "http://localhost:5000";
        _wsBaseUrl = _baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
    }

    /// <summary>
    /// Example 1: Stream messages from an ephemeral consumer using subject filter
    /// </summary>
    public async Task StreamFromEphemeralConsumer(string subjectFilter = "events.>", int maxMessages = 10)
    {
        Console.WriteLine($"=== Example 1: Streaming from Ephemeral Consumer ({subjectFilter}) ===");

        var wsUrl = $"{_wsBaseUrl}/ws/websocketmessages/{subjectFilter}";
        Console.WriteLine($"Connecting to: {wsUrl}");

        using var ws = new ClientWebSocket();
        var cts = new CancellationTokenSource();

        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), cts.Token);
            Console.WriteLine("✓ WebSocket connected");

            var messageCount = 0;

            while (ws.State == WebSocketState.Open && messageCount < maxMessages)
            {
                var buffer = new byte[1024 * 16]; // 16KB buffer
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var frameBytes = new byte[result.Count];
                    Array.Copy(buffer, frameBytes, result.Count);

                    // Parse the WebSocketFrame
                    var frame = WebSocketFrame.Parser.ParseFrom(frameBytes);

                    switch (frame.Type)
                    {
                        case FrameType.Control:
                            HandleControlMessage(frame.Control);
                            break;

                        case FrameType.Message:
                            HandleStreamMessage(frame.Message);
                            messageCount++;
                            break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"✓ Server closed connection: {result.CloseStatusDescription}");
                    break;
                }
            }

            Console.WriteLine($"✓ Received {messageCount} messages");

            // Close the WebSocket gracefully
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
        finally
        {
            cts.Cancel();
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Example 2: Stream messages from a durable consumer
    /// </summary>
    public async Task StreamFromDurableConsumer(
        string streamName = "EVENTS",
        string consumerName = "my-durable-consumer",
        int maxMessages = 10)
    {
        Console.WriteLine($"=== Example 2: Streaming from Durable Consumer ({consumerName}) ===");

        var wsUrl = $"{_wsBaseUrl}/ws/websocketmessages/{streamName}/consumer/{consumerName}";
        Console.WriteLine($"Connecting to: {wsUrl}");

        using var ws = new ClientWebSocket();
        var cts = new CancellationTokenSource();

        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), cts.Token);
            Console.WriteLine("✓ WebSocket connected");

            var messageCount = 0;

            while (ws.State == WebSocketState.Open && messageCount < maxMessages)
            {
                var buffer = new byte[1024 * 16]; // 16KB buffer
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var frameBytes = new byte[result.Count];
                    Array.Copy(buffer, frameBytes, result.Count);

                    // Parse the WebSocketFrame
                    var frame = WebSocketFrame.Parser.ParseFrom(frameBytes);

                    switch (frame.Type)
                    {
                        case FrameType.Control:
                            HandleControlMessage(frame.Control);
                            break;

                        case FrameType.Message:
                            HandleStreamMessage(frame.Message);
                            messageCount++;
                            break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"✓ Server closed connection: {result.CloseStatusDescription}");
                    break;
                }
            }

            Console.WriteLine($"✓ Received {messageCount} messages");

            // Close the WebSocket gracefully
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine($"  Make sure the durable consumer '{consumerName}' exists in stream '{streamName}'");
        }
        finally
        {
            cts.Cancel();
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Example 3: Stream with custom message processing and timeout
    /// </summary>
    public async Task StreamWithTimeout(string subjectFilter = "events.test", int timeoutSeconds = 30)
    {
        Console.WriteLine($"=== Example 3: Streaming with Timeout ({timeoutSeconds}s) ===");

        var wsUrl = $"{_wsBaseUrl}/ws/websocketmessages/{subjectFilter}";
        Console.WriteLine($"Connecting to: {wsUrl}");

        using var ws = new ClientWebSocket();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), cts.Token);
            Console.WriteLine("✓ WebSocket connected");
            Console.WriteLine($"  Will disconnect after {timeoutSeconds} seconds or when cancelled");

            var messageCount = 0;
            var startTime = DateTime.UtcNow;

            while (ws.State == WebSocketState.Open)
            {
                var buffer = new byte[1024 * 16];
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var frameBytes = new byte[result.Count];
                    Array.Copy(buffer, frameBytes, result.Count);

                    var frame = WebSocketFrame.Parser.ParseFrom(frameBytes);

                    switch (frame.Type)
                    {
                        case FrameType.Control:
                            HandleControlMessage(frame.Control);
                            break;

                        case FrameType.Message:
                            messageCount++;
                            var elapsed = DateTime.UtcNow - startTime;
                            Console.WriteLine($"  [{elapsed:mm\\:ss}] Message #{messageCount}: {frame.Message.Subject} (seq: {frame.Message.Sequence})");
                            break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"✓ Server closed connection: {result.CloseStatusDescription}");
                    break;
                }
            }

            Console.WriteLine($"✓ Stream ended - received {messageCount} messages");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"✓ Timeout reached after {timeoutSeconds} seconds");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Timeout", CancellationToken.None);
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Handle control messages from the server
    /// </summary>
    private void HandleControlMessage(ControlMessage control)
    {
        var icon = control.Type switch
        {
            ControlType.Error => "✗",
            ControlType.SubscribeAck => "✓",
            ControlType.Close => "✓",
            ControlType.Keepalive => "♥",
            _ => "•"
        };

        Console.WriteLine($"{icon} Control [{control.Type}]: {control.Message}");
    }

    /// <summary>
    /// Handle stream messages from NATS
    /// </summary>
    private void HandleStreamMessage(StreamMessage message)
    {
        Console.WriteLine($"  Message received:");
        Console.WriteLine($"    Subject:  {message.Subject}");
        Console.WriteLine($"    Sequence: {message.Sequence}");
        Console.WriteLine($"    Size:     {message.SizeBytes} bytes");

        if (message.Timestamp != null)
        {
            Console.WriteLine($"    Time:     {message.Timestamp.ToDateTime():yyyy-MM-dd HH:mm:ss.fff}");
        }

        if (!string.IsNullOrEmpty(message.Consumer))
        {
            Console.WriteLine($"    Consumer: {message.Consumer}");
        }

        // Try to decode data as UTF-8 string
        if (message.Data != null && message.Data.Length > 0)
        {
            try
            {
                var dataStr = message.Data.ToStringUtf8();
                var preview = dataStr.Length > 100 ? dataStr.Substring(0, 100) + "..." : dataStr;
                Console.WriteLine($"    Data:     {preview}");
            }
            catch
            {
                Console.WriteLine($"    Data:     [binary, {message.Data.Length} bytes]");
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Run all examples
    /// </summary>
    public async Task RunAllExamples()
    {
        Console.WriteLine($"WebSocket Client Example - Connecting to {_baseUrl}");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        try
        {
            // Example 1: Stream from ephemeral consumer
            await StreamFromEphemeralConsumer("events.>", maxMessages: 5);

            // Example 2: Stream from durable consumer (will fail if consumer doesn't exist)
            // Uncomment if you have a durable consumer set up:
            // await StreamFromDurableConsumer("EVENTS", "my-durable-consumer", maxMessages: 5);

            // Example 3: Stream with timeout
            await StreamWithTimeout("events.test", timeoutSeconds: 10);

            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("✓ All examples completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine($"  Make sure NatsHttpGateway is running at {_baseUrl}");
            Console.WriteLine($"  Make sure NATS is running and has messages to stream");
        }
    }
}

/// <summary>
/// Example program entry point for WebSocket client
/// </summary>
public class WebSocketProgram
{
    public static async Task Main(string[] args)
    {
        // Configuration priority: CLI arg > Environment variable > Default
        var baseUrl = args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable("NATS_GATEWAY_URL") ?? "http://localhost:5000";
        var client = new WebSocketClientExample(baseUrl);

        await client.RunAllExamples();

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
