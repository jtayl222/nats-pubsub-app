using System;
using System.Text;
using System.Text.Json;
using NATS.Client;

namespace NatsMessageLogger
{
    class Program
    {
        private static IConnection? _connection;
        private static readonly string _natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        private static readonly string _subject = Environment.GetEnvironmentVariable("NATS_SUBJECT") ?? ">";
        private static readonly string _hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? "message-logger";

        static void Main(string[] args)
        {
            LogInfo("Starting NATS Message Logger", new
            {
                nats_url = _natsUrl,
                subject = _subject,
                hostname = _hostname
            });

            try
            {
                ConnectToNats();
                SubscribeToMessages();

                // Keep running
                Console.WriteLine("Message logger running. Press Ctrl+C to exit.");
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    Cleanup();
                };

                // Block forever
                while (true)
                {
                    System.Threading.Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                LogError("Fatal error in message logger", ex);
                Environment.Exit(1);
            }
        }

        static void ConnectToNats()
        {
            var options = ConnectionFactory.GetDefaultOptions();
            options.Url = _natsUrl;
            options.Name = $"logger-{_hostname}";
            options.MaxReconnect = Options.ReconnectForever;
            options.ReconnectWait = 1000;
            options.DisconnectedEventHandler = (sender, args) =>
            {
                LogWarning("Disconnected from NATS", new { url = _natsUrl });
            };
            options.ReconnectedEventHandler = (sender, args) =>
            {
                LogInfo("Reconnected to NATS", new { url = _natsUrl });
            };

            var factory = new ConnectionFactory();
            _connection = factory.CreateConnection(options);

            LogInfo("Connected to NATS", new
            {
                url = _natsUrl,
                server_id = _connection.ConnectedId,
                server_url = _connection.ConnectedUrl
            });
        }

        static void SubscribeToMessages()
        {
            if (_connection == null) return;

            var subscription = _connection.SubscribeAsync(_subject, (sender, args) =>
            {
                try
                {
                    var rawMessage = Encoding.UTF8.GetString(args.Message.Data);

                    // Try to parse as JSON for structured logging
                    JsonDocument? jsonDoc = null;
                    try
                    {
                        jsonDoc = JsonDocument.Parse(rawMessage);
                    }
                    catch
                    {
                        // Not JSON, log as raw string
                        LogInfo("NATS message captured (raw)", new
                        {
                            subject = args.Message.Subject,
                            message = rawMessage,
                            size_bytes = args.Message.Data.Length
                        });
                        return;
                    }

                    // Determine log level based on subject and content
                    var subject = args.Message.Subject;
                    var logLevel = "INFO";

                    // Payment declined messages should be logged as ERROR
                    if (subject.Contains("declined") || subject.Contains("failed") || subject.Contains("error"))
                    {
                        logLevel = "ERROR";
                    }

                    // Also check message content for status
                    if (jsonDoc.RootElement.TryGetProperty("status", out var statusProp))
                    {
                        var status = statusProp.GetString();
                        if (status == "declined" || status == "failed" || status == "error")
                        {
                            logLevel = "ERROR";
                        }
                    }

                    // Extract key fields from the message
                    var messageData = new Dictionary<string, object>
                    {
                        ["subject"] = subject,
                        ["size_bytes"] = args.Message.Data.Length
                    };

                    // Extract relevant fields for better querying
                    if (jsonDoc.RootElement.TryGetProperty("transaction_id", out var txnId))
                        messageData["transaction_id"] = txnId.GetString() ?? "";

                    if (jsonDoc.RootElement.TryGetProperty("status", out var status))
                        messageData["status"] = status.GetString() ?? "";

                    if (jsonDoc.RootElement.TryGetProperty("decline_reason", out var reason))
                        messageData["decline_reason"] = reason.GetString() ?? "";

                    if (jsonDoc.RootElement.TryGetProperty("amount", out var amount))
                        messageData["amount"] = amount.GetDouble();

                    if (jsonDoc.RootElement.TryGetProperty("card_type", out var cardType))
                        messageData["card_type"] = cardType.GetString() ?? "";

                    if (jsonDoc.RootElement.TryGetProperty("event_type", out var eventType))
                        messageData["event_type"] = eventType.GetString() ?? "";

                    // Include full message for reference
                    messageData["payload"] = JsonSerializer.Deserialize<object>(rawMessage);

                    // Log at appropriate level
                    if (logLevel == "ERROR")
                    {
                        LogError("Payment transaction declined", messageData);
                    }
                    else
                    {
                        LogInfo("Payment transaction captured", messageData);
                    }

                    jsonDoc.Dispose();
                }
                catch (Exception ex)
                {
                    LogError("Error processing message", ex);
                }
            });

            LogInfo("Subscribed to messages", new
            {
                subject = _subject,
                subscription_id = subscription.GetHashCode()
            });
        }

        static void Cleanup()
        {
            LogInfo("Shutting down message logger", new { hostname = _hostname });
            _connection?.Close();
            _connection?.Dispose();
        }

        static void LogInfo(string message, object? data = null)
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = "INFO",
                logger = "nats-message-logger",
                message = message,
                hostname = _hostname,
                data = data
            };
            Console.WriteLine(JsonSerializer.Serialize(logEntry));
        }

        static void LogWarning(string message, object? data = null)
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = "WARN",
                logger = "nats-message-logger",
                message = message,
                hostname = _hostname,
                data = data
            };
            Console.WriteLine(JsonSerializer.Serialize(logEntry));
        }

        static void LogError(string message, object? data = null)
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = "ERROR",
                logger = "nats-message-logger",
                message = message,
                hostname = _hostname,
                data = data
            };
            Console.WriteLine(JsonSerializer.Serialize(logEntry));
        }

        static void LogError(string message, Exception ex)
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = "ERROR",
                logger = "nats-message-logger",
                message = message,
                hostname = _hostname,
                error = ex.Message,
                stacktrace = ex.StackTrace
            };
            Console.WriteLine(JsonSerializer.Serialize(logEntry));
        }
    }
}
