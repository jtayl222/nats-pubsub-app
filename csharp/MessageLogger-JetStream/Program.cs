using System;
using System.Text;
using System.Text.Json;
using NATS.Net;

namespace MessageLoggerJetStream
{
    class Program
    {
        private static readonly string _natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        private static readonly string _streamName = Environment.GetEnvironmentVariable("STREAM_NAME") ?? "PAYMENTS";
        private static readonly string _consumerName = Environment.GetEnvironmentVariable("CONSUMER_NAME") ?? "payment-monitor";
        private static readonly string _hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? "message-logger-js";
        private static readonly bool _replayHistory = bool.Parse(Environment.GetEnvironmentVariable("REPLAY_HISTORY") ?? "true");

        static async Task Main(string[] args)
        {
            LogInfo("Starting NATS Message Logger (JetStream)", new
            {
                nats_url = _natsUrl,
                stream = _streamName,
                consumer = _consumerName,
                hostname = _hostname,
                replay_history = _replayHistory
            });

            try
            {
                await using var nats = new NatsClient(_natsUrl);
                await nats.ConnectAsync();

                LogInfo("Connected to NATS", new
                {
                    url = _natsUrl,
                    server_info = nats.ServerInfo?.Version
                });

                // Create JetStream context
                var js = nats.CreateJetStreamContext();

                // Subscribe to stream with consumer
                await ConsumeMessages(js);
            }
            catch (Exception ex)
            {
                LogError("Fatal error in message logger", ex);
                Environment.Exit(1);
            }
        }

        static async Task ConsumeMessages(INatsJetStreamContext js)
        {
            try
            {
                // Check if stream exists
                var streamInfo = await js.GetStreamAsync(_streamName);
                LogInfo("JetStream stream found", new
                {
                    stream = _streamName,
                    subjects = streamInfo.Config.Subjects,
                    messages = streamInfo.State.Messages,
                    first_seq = streamInfo.State.FirstSeq,
                    last_seq = streamInfo.State.LastSeq
                });

                // Configure consumer
                var consumerConfig = new ConsumerConfig
                {
                    Name = _consumerName,
                    DurableName = _consumerName,
                    AckPolicy = ConsumerConfigAckPolicy.Explicit,
                    DeliverPolicy = _replayHistory
                        ? ConsumerConfigDeliverPolicy.All  // Replay from beginning
                        : ConsumerConfigDeliverPolicy.New,  // Only new messages
                    FilterSubject = "payments.>",
                    MaxDeliver = 3,
                    AckWait = TimeSpan.FromSeconds(30)
                };

                // Create or update consumer
                INatsJSConsumer consumer;
                try
                {
                    consumer = await js.CreateOrUpdateConsumerAsync(_streamName, consumerConfig);
                    LogInfo("JetStream consumer created", new
                    {
                        consumer = _consumerName,
                        deliver_policy = _replayHistory ? "all (replay history)" : "new (real-time only)",
                        filter = "payments.>"
                    });
                }
                catch (Exception ex)
                {
                    LogError("Failed to create consumer", ex);
                    throw;
                }

                // Consume messages
                LogInfo("Starting message consumption...", new
                {
                    consumer = _consumerName,
                    stream = _streamName
                });

                await foreach (var msg in consumer.ConsumeAsync<byte[]>())
                {
                    try
                    {
                        ProcessMessage(msg);
                        await msg.AckAsync();  // Acknowledge message
                    }
                    catch (Exception ex)
                    {
                        LogError("Error processing message", ex);
                        await msg.NakAsync();  // Negative acknowledgment - will redeliver
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Consumer error", ex);
                throw;
            }
        }

        static void ProcessMessage(NatsJSMsg<byte[]> msg)
        {
            var rawMessage = Encoding.UTF8.GetString(msg.Data);

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
                    subject = msg.Subject,
                    message = rawMessage,
                    size_bytes = msg.Data.Length,
                    js_sequence = msg.Metadata?.Sequence.Stream,
                    js_timestamp = msg.Metadata?.Timestamp
                });
                return;
            }

            // Determine log level based on subject and content
            var subject = msg.Subject;
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
                ["size_bytes"] = msg.Data.Length,
                ["js_sequence"] = msg.Metadata?.Sequence.Stream ?? 0,
                ["js_stream"] = msg.Metadata?.Sequence.Consumer ?? 0,
                ["js_timestamp"] = msg.Metadata?.Timestamp.ToString("o") ?? "",
                ["js_pending"] = msg.Metadata?.NumPending ?? 0
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

        static void LogInfo(string message, object? data = null)
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = "INFO",
                logger = "nats-message-logger-jetstream",
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
                logger = "nats-message-logger-jetstream",
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
                logger = "nats-message-logger-jetstream",
                message = message,
                hostname = _hostname,
                error = ex.Message,
                stacktrace = ex.StackTrace
            };
            Console.WriteLine(JsonSerializer.Serialize(logEntry));
        }
    }
}
