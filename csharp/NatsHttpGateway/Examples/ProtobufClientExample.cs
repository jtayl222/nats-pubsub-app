using NatsHttpGateway.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using System.Net.Http.Headers;

namespace NatsHttpGateway.Examples;

/// <summary>
/// Example client demonstrating how to use protobuf endpoints
/// </summary>
public class ProtobufClientExample
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public ProtobufClientExample(string baseUrl = "http://localhost:8080")
    {
        _baseUrl = baseUrl;
        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    /// <summary>
    /// Example 1: Publish a generic protobuf message
    /// </summary>
    public async Task PublishGenericMessage()
    {
        Console.WriteLine("=== Example 1: Publishing Generic Message ===");

        // Create a PublishMessage
        var message = new PublishMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Subject = "events.test",
            Source = "protobuf-example",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Data = ByteString.CopyFromUtf8("{\"message\":\"Hello from Protobuf!\"}")
        };
        message.Metadata.Add("client", "csharp");
        message.Metadata.Add("version", "1.0");

        // Serialize to protobuf binary
        var protobufBytes = message.ToByteArray();
        Console.WriteLine($"Protobuf payload size: {protobufBytes.Length} bytes");

        // Send to gateway
        var content = new ByteArrayContent(protobufBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        var response = await _client.PostAsync(
            "/api/proto/ProtobufMessages/events.test",
            content
        );

        response.EnsureSuccessStatusCode();

        // Parse response
        var responseBytes = await response.Content.ReadAsByteArrayAsync();
        var ack = PublishAck.Parser.ParseFrom(responseBytes);

        Console.WriteLine($"✓ Published successfully!");
        Console.WriteLine($"  Stream: {ack.Stream}");
        Console.WriteLine($"  Sequence: {ack.Sequence}");
        Console.WriteLine($"  Subject: {ack.Subject}");
        Console.WriteLine();
    }

    /// <summary>
    /// Example 2: Publish a UserEvent
    /// </summary>
    public async Task PublishUserEvent()
    {
        Console.WriteLine("=== Example 2: Publishing UserEvent ===");

        var userEvent = new UserEvent
        {
            UserId = $"user-{new Random().Next(1000, 9999)}",
            EventType = "created",
            Email = "newuser@example.com",
            OccurredAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        userEvent.Attributes.Add("plan", "premium");
        userEvent.Attributes.Add("referral_code", "PROTO123");

        var content = new ByteArrayContent(userEvent.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        var response = await _client.PostAsync(
            "/api/proto/ProtobufMessages/events.user.created/user-event",
            content
        );

        response.EnsureSuccessStatusCode();

        var ack = PublishAck.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());

        Console.WriteLine($"✓ UserEvent published!");
        Console.WriteLine($"  User ID: {userEvent.UserId}");
        Console.WriteLine($"  Event Type: {userEvent.EventType}");
        Console.WriteLine($"  Stream: {ack.Stream}, Sequence: {ack.Sequence}");
        Console.WriteLine();
    }

    /// <summary>
    /// Example 3: Publish a PaymentEvent
    /// </summary>
    public async Task PublishPaymentEvent()
    {
        Console.WriteLine("=== Example 3: Publishing PaymentEvent ===");

        var paymentEvent = new PaymentEvent
        {
            TransactionId = $"txn-{Guid.NewGuid():N}",
            Status = "approved",
            Amount = 129.99,
            Currency = "USD",
            CardLastFour = "4242",
            ProcessedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        var content = new ByteArrayContent(paymentEvent.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        var response = await _client.PostAsync(
            "/api/proto/ProtobufMessages/payments.credit_card.approved/payment-event",
            content
        );

        response.EnsureSuccessStatusCode();

        var ack = PublishAck.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());

        Console.WriteLine($"✓ PaymentEvent published!");
        Console.WriteLine($"  Transaction ID: {paymentEvent.TransactionId}");
        Console.WriteLine($"  Amount: ${paymentEvent.Amount} {paymentEvent.Currency}");
        Console.WriteLine($"  Status: {paymentEvent.Status}");
        Console.WriteLine($"  Stream: {ack.Stream}, Sequence: {ack.Sequence}");
        Console.WriteLine();
    }

    /// <summary>
    /// Example 4: Fetch messages in protobuf format
    /// </summary>
    public async Task FetchMessages(string subject = "events.test", int limit = 5)
    {
        Console.WriteLine($"=== Example 4: Fetching Messages ({subject}) ===");

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/proto/ProtobufMessages/{subject}?limit={limit}"
        );
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-protobuf"));

        var response = await _client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"✗ Failed to fetch: {response.StatusCode}");
            return;
        }

        var responseBytes = await response.Content.ReadAsByteArrayAsync();
        var fetchResponse = FetchResponse.Parser.ParseFrom(responseBytes);

        Console.WriteLine($"✓ Fetched {fetchResponse.Count} messages from {fetchResponse.Stream}");
        Console.WriteLine($"  Subject: {fetchResponse.Subject}");
        Console.WriteLine($"  Messages:");

        foreach (var msg in fetchResponse.Messages)
        {
            Console.WriteLine($"    [{msg.Sequence}] {msg.Subject}");
            Console.WriteLine($"        Size: {msg.SizeBytes} bytes");
            Console.WriteLine($"        Time: {msg.Timestamp.ToDateTime():yyyy-MM-dd HH:mm:ss}");

            // Try to decode data as UTF-8 string
            try
            {
                var dataStr = msg.Data.ToStringUtf8();
                var preview = dataStr.Length > 50 ? dataStr.Substring(0, 50) + "..." : dataStr;
                Console.WriteLine($"        Data: {preview}");
            }
            catch
            {
                Console.WriteLine($"        Data: [binary, {msg.Data.Length} bytes]");
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Run all examples
    /// </summary>
    public async Task RunAllExamples()
    {
        Console.WriteLine($"Protobuf Client Example - Connecting to {_baseUrl}");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();

        try
        {
            await PublishGenericMessage();
            await PublishUserEvent();
            await PublishPaymentEvent();
            await FetchMessages("events.test", 5);
            await FetchMessages("events.user.created", 3);

            Console.WriteLine("=".PadRight(60, '='));
            Console.WriteLine("✓ All examples completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine($"  Make sure NatsHttpGateway is running at {_baseUrl}");
        }
    }
}

/// <summary>
/// Example program entry point
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var baseUrl = args.Length > 0 ? args[0] : "http://localhost:8080";
        var client = new ProtobufClientExample(baseUrl);

        await client.RunAllExamples();

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
