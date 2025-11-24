using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NatsSubscriber
{
    class Program
    {
        private static long _messageCount = 0;
        private static long _errorCount = 0;
        private static DateTime _startTime = DateTime.UtcNow;
        private static DateTime? _lastMessageTime = null;
        private static double _totalLatencyMs = 0;
        private static readonly string _natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        private static readonly string _subject = Environment.GetEnvironmentVariable("NATS_SUBJECT") ?? "events.test";
        private static readonly string _streamName = Environment.GetEnvironmentVariable("STREAM_NAME") ?? "EVENTS";
        private static readonly string _consumerName = Environment.GetEnvironmentVariable("CONSUMER_NAME") ?? "subscriber-consumer";
        private static readonly string _hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? "csharp-subscriber";
        private static readonly string? _queueGroup = Environment.GetEnvironmentVariable("QUEUE_GROUP");

        static async Task Main(string[] args)
        {
            LogInfo("Starting NATS Subscriber (JetStream)", new
            {
                nats_url = _natsUrl,
                subject = _subject,
                stream = _streamName,
                consumer = _consumerName,
                hostname = _hostname,
                queue_group = _queueGroup ?? "none"
            });

            try
            {
                var opts = new NatsOpts { Url = _natsUrl };
                await using var nats = new NatsConnection(opts);
                await nats.ConnectAsync();

                LogInfo("Connected to NATS", new
                {
                    url = _natsUrl,
                    server_info = nats.ServerInfo?.ToString()
                });

                // Create JetStream context
                var js = new NatsJSContext(nats);

                // Ensure stream exists (create if needed)
                await EnsureStreamExists(js);

                // Fetch last 10 messages first
                await FetchRecentMessages(js);

                // Then subscribe for ongoing messages
                await Subscribe(js);

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

        static async Task EnsureStreamExists(INatsJSContext js)
        {
            try
            {
                // Try to get stream info
                await js.GetStreamAsync(_streamName);
                LogInfo("Stream already exists", new { stream = _streamName });
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                // Stream doesn't exist, create it
                LogInfo("Stream not found, creating it", new { stream = _streamName, subject = _subject });

                var streamConfig = new StreamConfig(_streamName, new[] { _subject })
                {
                    Description = "Event stream for subscriber",
                    Retention = StreamConfigRetention.Limits,
                    MaxMsgs = 1000,
                    MaxBytes = 1024 * 1024 * 10, // 10MB
                    MaxAge = TimeSpan.FromHours(24),
                    Storage = StreamConfigStorage.File,
                    NumReplicas = 1
                };

                await js.CreateStreamAsync(streamConfig);
                LogInfo("Stream created successfully", new { stream = _streamName });
            }
            catch (Exception ex)
            {
                LogError("Error ensuring stream exists", ex);
                throw;
            }
        }

        static async Task FetchRecentMessages(INatsJSContext js)
        {
            try
            {
                LogInfo("Fetching last 10 messages from stream", new
                {
                    stream = _streamName,
                    subject = _subject
                });

                // Create a temporary consumer configured to deliver last N messages
                var fetchConsumerConfig = new ConsumerConfig
                {
                    Name = $"{_consumerName}-fetch-{Guid.NewGuid()}",
                    DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject,
                    FilterSubject = _subject,
                    AckPolicy = ConsumerConfigAckPolicy.None,
                    InactiveThreshold = TimeSpan.FromSeconds(10)
                };

                var fetchConsumer = await js.CreateConsumerAsync(_streamName, fetchConsumerConfig);

                // Fetch up to 10 messages
                var fetchedCount = 0;
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                try
                {
                    await foreach (var msg in fetchConsumer.FetchAsync<byte[]>(opts: new NatsJSFetchOpts { MaxMsgs = 10, Expires = TimeSpan.FromSeconds(5) }, cancellationToken: cts.Token))
                    {
                        ProcessMessage(msg, isHistory: true);
                        fetchedCount++;

                        if (fetchedCount >= 10)
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timeout is expected if there are fewer than 10 messages
                }
                catch (Exception ex)
                {
                    LogError("Error during fetch", ex);
                }

                LogInfo("Finished fetching recent messages", new
                {
                    fetched_count = fetchedCount,
                    stream = _streamName
                });

                // Delete the temporary consumer
                try
                {
                    await js.DeleteConsumerAsync(_streamName, fetchConsumerConfig.Name);
                }
                catch
                {
                    // Ignore errors deleting temp consumer
                }
            }
            catch (Exception ex)
            {
                LogError("Error fetching recent messages", ex);
                LogInfo("Continuing with subscription", new { });
            }
        }

        static async Task Subscribe(INatsJSContext js)
        {
            LogInfo("Subscribing to stream for new messages", new
            {
                stream = _streamName,
                subject = _subject,
                consumer = _consumerName
            });

            try
            {
                // Configure consumer for new messages only
                var consumerConfig = new ConsumerConfig
                {
                    Name = _consumerName,
                    DurableName = _consumerName,
                    AckPolicy = ConsumerConfigAckPolicy.Explicit,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.New, // Only new messages
                    FilterSubject = _subject,
                    MaxDeliver = 3,
                    AckWait = TimeSpan.FromSeconds(30)
                };

                // Create or update consumer
                var consumer = await js.CreateOrUpdateConsumerAsync(_streamName, consumerConfig);

                LogInfo("Subscription active", new
                {
                    consumer = _consumerName,
                    stream = _streamName,
                    subject = _subject
                });

                // Consume messages
                await foreach (var msg in consumer.ConsumeAsync<byte[]>())
                {
                    try
                    {
                        ProcessMessage(msg, isHistory: false);
                        await msg.AckAsync();
                    }
                    catch (Exception ex)
                    {
                        _errorCount++;
                        LogError("Error processing message", ex);
                        await msg.NakAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Failed to subscribe", ex);
                throw;
            }
        }

        static void ProcessMessage(NatsJSMsg<byte[]> msg, bool isHistory)
        {
            var receiveTime = DateTime.UtcNow;
            _messageCount++;
            _lastMessageTime = receiveTime;

            try
            {
                if (msg.Data == null)
                {
                    _errorCount++;
                    LogWarning("Message has null data", new { subject = msg.Subject });
                    return;
                }

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

                LogInfo(isHistory ? "Historical message received" : "Message received", new
                {
                    message_id = message.MessageId,
                    subject = msg.Subject,
                    size_bytes = msg.Data.Length,
                    source = message.Source,
                    sequence = message.Sequence,
                    js_sequence = msg.Metadata?.Sequence.Stream,
                    js_timestamp = msg.Metadata?.Timestamp,
                    latency_ms = Math.Round(latencyMs, 2),
                    event_type = message.Data?.EventType ?? "unknown",
                    is_history = isHistory
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
                logger = "nats-subscriber-jetstream",
                message = message,
                hostname = _hostname,
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
