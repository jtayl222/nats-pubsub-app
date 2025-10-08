using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NATS.Client;

namespace NatsPublisher
{
    class Program
    {
        private static IConnection? _connection;
        private static long _messageCount = 0;
        private static long _errorCount = 0;
        private static DateTime _startTime = DateTime.UtcNow;
        private static readonly string _natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        private static readonly string _subject = Environment.GetEnvironmentVariable("NATS_SUBJECT") ?? "events.test";
        private static readonly string _hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? "csharp-publisher";
        private static readonly double _publishInterval = double.Parse(Environment.GetEnvironmentVariable("PUBLISH_INTERVAL") ?? "2.0");

        static async Task Main(string[] args)
        {
            LogInfo("Starting NATS Publisher", new { nats_url = _natsUrl, subject = _subject, hostname = _hostname });

            try
            {
                await ConnectToNats();
                await PublishLoop();
            }
            catch (Exception ex)
            {
                LogError("Fatal error in publisher", ex);
                Environment.Exit(1);
            }
        }

        static async Task ConnectToNats()
        {
            LogInfo("Connecting to NATS", new { nats_url = _natsUrl });

            try
            {
                var options = ConnectionFactory.GetDefaultOptions();
                options.Url = _natsUrl;
                options.Name = $"publisher-{_hostname}";
                options.ReconnectWait = 2000;
                options.MaxReconnect = 60;
                options.PingInterval = 20000;
                options.MaxPingsOut = 3;

                options.DisconnectedEventHandler = (sender, args) =>
                {
                    LogWarning("Disconnected from NATS", new { });
                };

                options.ReconnectedEventHandler = (sender, args) =>
                {
                    LogInfo("Reconnected to NATS", new { });
                };

                options.ClosedEventHandler = (sender, args) =>
                {
                    LogWarning("Connection closed", new { });
                };

                var factory = new ConnectionFactory();
                _connection = factory.CreateConnection(options);

                LogInfo("Connected to NATS successfully", new
                {
                    server_info = _connection.ConnectedUrl,
                    client_id = _connection.ClientID
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogError("Failed to connect to NATS", ex);
                throw;
            }
        }

        static async Task PublishLoop()
        {
            LogInfo("Starting publish loop", new { interval_seconds = _publishInterval, subject = _subject });

            var random = new Random();
            var eventTypes = new[] { "user.login", "user.logout", "order.created", "payment.processed" };
            var randomFields = new[] { "alpha", "beta", "gamma", "delta" };

            while (true)
            {
                try
                {
                    _messageCount++;

                    var message = new MessageData
                    {
                        MessageId = $"{_hostname}-{_messageCount}",
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        Source = _hostname,
                        Sequence = _messageCount,
                        Data = new MessagePayload
                        {
                            EventType = eventTypes[random.Next(eventTypes.Length)],
                            Value = random.Next(1, 1001),
                            RandomField = randomFields[random.Next(randomFields.Length)]
                        }
                    };

                    var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    var bytes = Encoding.UTF8.GetBytes(json);

                    _connection?.Publish(_subject, bytes);

                    LogInfo("Message published", new
                    {
                        message_id = message.MessageId,
                        subject = _subject,
                        size_bytes = bytes.Length,
                        sequence = _messageCount,
                        event_type = message.Data.EventType
                    });

                    // Log metrics every 50 messages
                    if (_messageCount % 50 == 0)
                    {
                        LogMetrics();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(_publishInterval));
                }
                catch (NATSConnectionClosedException)
                {
                    _errorCount++;
                    LogWarning("NATS connection closed, waiting for reconnect", new { });
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _errorCount++;
                    LogError("Publish error", ex);
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
        }

        static void LogMetrics()
        {
            var uptime = (DateTime.UtcNow - _startTime).TotalSeconds;
            var messagesPerSecond = uptime > 0 ? _messageCount / uptime : 0;
            var errorRate = _messageCount > 0 ? (_errorCount / (double)_messageCount) * 100 : 0;

            LogInfo("Publisher metrics", new
            {
                total_messages = _messageCount,
                total_errors = _errorCount,
                uptime_seconds = Math.Round(uptime, 2),
                messages_per_second = Math.Round(messagesPerSecond, 2),
                error_rate = Math.Round(errorRate, 2)
            });
        }

        static void LogInfo(string message, object extraFields)
        {
            WriteLog("INFO", message, extraFields, null);
        }

        static void LogWarning(string message, object extraFields)
        {
            WriteLog("WARN", message, extraFields, null);
        }

        static void LogError(string message, Exception? ex)
        {
            WriteLog("ERROR", message, new { }, ex);
        }

        static void WriteLog(string level, string message, object extraFields, Exception? ex)
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = level,
                logger = "nats-publisher",
                message = message,
                module = "Program",
                function = new System.Diagnostics.StackTrace().GetFrame(2)?.GetMethod()?.Name ?? "Unknown"
            };

            var logData = JsonSerializer.SerializeToElement(logEntry);
            var extraData = JsonSerializer.SerializeToElement(extraFields);

            var combined = new Dictionary<string, JsonElement>();
            foreach (var prop in logData.EnumerateObject())
            {
                combined[prop.Name] = prop.Value;
            }
            foreach (var prop in extraData.EnumerateObject())
            {
                combined[prop.Name] = prop.Value;
            }

            if (ex != null)
            {
                combined["exception"] = JsonSerializer.SerializeToElement(ex.ToString());
            }

            var json = JsonSerializer.Serialize(combined);
            Console.WriteLine(json);
        }
    }

    class MessageData
    {
        [JsonPropertyName("message_id")]
        public string MessageId { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("sequence")]
        public long Sequence { get; set; }

        [JsonPropertyName("data")]
        public MessagePayload Data { get; set; } = new MessagePayload();
    }

    class MessagePayload
    {
        [JsonPropertyName("event_type")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public int Value { get; set; }

        [JsonPropertyName("random_field")]
        public string RandomField { get; set; } = string.Empty;
    }
}
