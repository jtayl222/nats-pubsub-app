using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NATS.Client;

namespace NatsSubscriber
{
    class Program
    {
        private static IConnection? _connection;
        private static long _messageCount = 0;
        private static long _errorCount = 0;
        private static DateTime _startTime = DateTime.UtcNow;
        private static DateTime? _lastMessageTime = null;
        private static double _totalLatencyMs = 0;
        private static readonly string _natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        private static readonly string _subject = Environment.GetEnvironmentVariable("NATS_SUBJECT") ?? "events.test";
        private static readonly string _hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? "csharp-subscriber";
        private static readonly string? _queueGroup = Environment.GetEnvironmentVariable("QUEUE_GROUP");

        static async Task Main(string[] args)
        {
            LogInfo("Starting NATS Subscriber", new
            {
                nats_url = _natsUrl,
                subject = _subject,
                hostname = _hostname,
                queue_group = _queueGroup ?? "none"
            });

            try
            {
                await ConnectToNats();
                await Subscribe();

                // Metrics logging task
                var metricsTask = Task.Run(async () =>
                {
                    while (true)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(60));
                        LogMetrics();
                    }
                });

                // Keep running
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                LogError("Fatal error in subscriber", ex);
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
                options.Name = $"subscriber-{_hostname}";
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

        static async Task Subscribe()
        {
            LogInfo("Subscribing to subject", new
            {
                subject = _subject,
                queue_group = _queueGroup ?? "none"
            });

            try
            {
                EventHandler<MsgHandlerEventArgs> handler = (sender, args) =>
                {
                    HandleMessage(args.Message);
                };

                IAsyncSubscription subscription;
                if (!string.IsNullOrEmpty(_queueGroup))
                {
                    subscription = _connection!.SubscribeAsync(_subject, _queueGroup, handler);
                }
                else
                {
                    subscription = _connection!.SubscribeAsync(_subject, handler);
                }

                LogInfo("Subscription active", new
                {
                    subject = _subject,
                    queue_group = _queueGroup ?? "none"
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogError("Failed to subscribe", ex);
                throw;
            }
        }

        static void HandleMessage(Msg msg)
        {
            var receiveTime = DateTime.UtcNow;
            _messageCount++;
            _lastMessageTime = receiveTime;

            try
            {
                var json = Encoding.UTF8.GetString(msg.Data);
                var message = JsonSerializer.Deserialize<MessageData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (message == null)
                {
                    _errorCount++;
                    LogWarning("Failed to deserialize message", new { subject = msg.Subject });
                    return;
                }

                // Calculate latency
                var messageTimestamp = DateTime.Parse(message.Timestamp);
                var latencyMs = (receiveTime - messageTimestamp).TotalMilliseconds;
                _totalLatencyMs += latencyMs;

                LogInfo("Message received", new
                {
                    message_id = message.MessageId,
                    subject = msg.Subject,
                    size_bytes = msg.Data.Length,
                    source = message.Source,
                    sequence = message.Sequence,
                    latency_ms = Math.Round(latencyMs, 2),
                    event_type = message.Data?.EventType ?? "unknown",
                    reply_to = msg.Reply ?? ""
                });

                // Log metrics every 50 messages
                if (_messageCount % 50 == 0)
                {
                    LogMetrics();
                }
            }
            catch (JsonException ex)
            {
                _errorCount++;
                LogError("Failed to decode message JSON", ex);
            }
            catch (Exception ex)
            {
                _errorCount++;
                LogError("Error processing message", ex);
            }
        }

        static void LogMetrics()
        {
            var uptime = (DateTime.UtcNow - _startTime).TotalSeconds;
            var messagesPerSecond = uptime > 0 ? _messageCount / uptime : 0;
            var avgLatency = _messageCount > 0 ? _totalLatencyMs / _messageCount : 0;
            var errorRate = _messageCount > 0 ? (_errorCount / (double)_messageCount) * 100 : 0;

            LogInfo("Subscriber metrics", new
            {
                total_messages = _messageCount,
                total_errors = _errorCount,
                uptime_seconds = Math.Round(uptime, 2),
                messages_per_second = Math.Round(messagesPerSecond, 2),
                average_latency_ms = Math.Round(avgLatency, 2),
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
                logger = "nats-subscriber",
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
        public MessagePayload? Data { get; set; }
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
