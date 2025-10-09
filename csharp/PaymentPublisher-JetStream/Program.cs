using System;
using System.Text;
using System.Text.Json;
using NATS.Net;

namespace PaymentPublisherJetStream
{
    class Program
    {
        private static readonly string _natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        private static readonly string _hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? "payment-publisher-js";
        private static readonly double _publishInterval = double.Parse(Environment.GetEnvironmentVariable("PUBLISH_INTERVAL") ?? "5.0");
        private static readonly string _streamName = "PAYMENTS";

        private static int _transactionCount = 0;
        private static int _acceptedCount = 0;
        private static int _declinedCount = 0;
        private static readonly DateTime _startTime = DateTime.UtcNow;
        private static DateTime _lastErrorTime = DateTime.UtcNow;
        private static readonly Random _random = new Random();

        static async Task Main(string[] args)
        {
            LogInfo("Starting Payment Publisher (JetStream)", new
            {
                nats_url = _natsUrl,
                hostname = _hostname,
                publish_interval = _publishInterval,
                stream = _streamName
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

                // Ensure stream exists
                await EnsureStreamExists(js);

                // Start publishing loop
                await PublishLoop(js);
            }
            catch (Exception ex)
            {
                LogError("Fatal error in payment publisher", ex);
                Environment.Exit(1);
            }
        }

        static async Task EnsureStreamExists(INatsJetStreamContext js)
        {
            try
            {
                // Try to get stream info
                var streamInfo = await js.GetStreamAsync(_streamName);
                LogInfo("JetStream stream already exists", new
                {
                    stream = _streamName,
                    subjects = streamInfo.Config.Subjects,
                    messages = streamInfo.State.Messages
                });
            }
            catch
            {
                // Stream doesn't exist, create it
                LogInfo("Creating JetStream stream", new { stream = _streamName });

                var streamConfig = new StreamConfig
                {
                    Name = _streamName,
                    Subjects = new[] { "payments.>" },
                    Storage = StreamConfigStorage.File,
                    Retention = StreamConfigRetention.Limits,
                    MaxAge = TimeSpan.FromDays(7),
                    MaxBytes = 1_000_000_000, // 1GB
                    Discard = StreamConfigDiscard.Old
                };

                await js.CreateStreamAsync(streamConfig);

                LogInfo("JetStream stream created", new
                {
                    stream = _streamName,
                    subjects = string.Join(", ", streamConfig.Subjects),
                    retention = "7 days"
                });
            }
        }

        static async Task PublishLoop(INatsJetStreamContext js)
        {
            LogInfo("Starting payment transaction loop", new
            {
                interval_seconds = _publishInterval,
                error_frequency = "~1 per minute",
                persistence = "JetStream"
            });

            while (true)
            {
                try
                {
                    _transactionCount++;

                    // Determine if this should be a declined transaction
                    var messagesPerMinute = (int)(60.0 / _publishInterval);
                    var timeSinceLastError = (DateTime.UtcNow - _lastErrorTime).TotalSeconds;

                    bool shouldDecline = timeSinceLastError >= 60 || _random.Next(messagesPerMinute) == 0;

                    if (shouldDecline)
                    {
                        await PublishDeclinedTransaction(js);
                        _lastErrorTime = DateTime.UtcNow;
                    }
                    else
                    {
                        await PublishAcceptedTransaction(js);
                    }

                    // Log metrics every 20 transactions
                    if (_transactionCount % 20 == 0)
                    {
                        LogInfo("Payment publisher metrics", new
                        {
                            total_transactions = _transactionCount,
                            accepted = _acceptedCount,
                            declined = _declinedCount,
                            decline_rate = Math.Round((_declinedCount / (double)_transactionCount) * 100, 2),
                            uptime_seconds = (DateTime.UtcNow - _startTime).TotalSeconds,
                            transactions_per_second = Math.Round(_transactionCount / (DateTime.UtcNow - _startTime).TotalSeconds, 2)
                        });
                    }

                    await Task.Delay(TimeSpan.FromSeconds(_publishInterval));
                }
                catch (Exception ex)
                {
                    LogError("Publish error", ex);
                    await Task.Delay(5000);
                }
            }
        }

        static async Task PublishAcceptedTransaction(INatsJetStreamContext js)
        {
            _acceptedCount++;

            var cardTypes = new[] { "Visa", "Mastercard", "Amex", "Discover" };
            var amounts = new[] { 29.99, 49.99, 99.99, 149.99, 249.99, 499.99 };

            var transaction = new
            {
                transaction_id = $"TXN-{_hostname}-{_transactionCount}",
                timestamp = DateTime.UtcNow.ToString("o"),
                source = _hostname,
                card_type = cardTypes[_random.Next(cardTypes.Length)],
                last_four = _random.Next(1000, 9999).ToString(),
                amount = amounts[_random.Next(amounts.Length)],
                currency = "USD",
                status = "accepted",
                merchant_id = $"MERCH-{_random.Next(1000, 9999)}",
                authorization_code = $"AUTH-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}"
            };

            var json = JsonSerializer.Serialize(transaction);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Publish to JetStream (persisted)
            var ack = await js.PublishAsync("payments.credit_card.accepted", bytes);

            LogInfo("Credit card accepted (JetStream)", new
            {
                transaction_id = transaction.transaction_id,
                subject = "payments.credit_card.accepted",
                card_type = transaction.card_type,
                last_four = transaction.last_four,
                amount = transaction.amount,
                authorization_code = transaction.authorization_code,
                js_sequence = ack.Seq,
                js_stream = ack.Stream,
                size_bytes = bytes.Length
            });
        }

        static async Task PublishDeclinedTransaction(INatsJetStreamContext js)
        {
            _declinedCount++;

            var cardTypes = new[] { "Visa", "Mastercard", "Amex", "Discover" };
            var amounts = new[] { 29.99, 49.99, 99.99, 149.99, 249.99, 499.99 };
            var declineReasons = new[]
            {
                "insufficient_funds",
                "card_expired",
                "invalid_cvv",
                "suspected_fraud",
                "card_limit_exceeded",
                "issuer_declined"
            };

            var transaction = new
            {
                transaction_id = $"TXN-{_hostname}-{_transactionCount}",
                timestamp = DateTime.UtcNow.ToString("o"),
                source = _hostname,
                card_type = cardTypes[_random.Next(cardTypes.Length)],
                last_four = _random.Next(1000, 9999).ToString(),
                amount = amounts[_random.Next(amounts.Length)],
                currency = "USD",
                status = "declined",
                decline_reason = declineReasons[_random.Next(declineReasons.Length)],
                merchant_id = $"MERCH-{_random.Next(1000, 9999)}",
                decline_code = $"ERR-{_random.Next(100, 999)}"
            };

            var json = JsonSerializer.Serialize(transaction);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Publish to JetStream (persisted)
            var ack = await js.PublishAsync("payments.credit_card.declined", bytes);

            // Log as ERROR level so it shows up prominently in Loki
            LogError("Credit card declined (JetStream)", new
            {
                transaction_id = transaction.transaction_id,
                subject = "payments.credit_card.declined",
                card_type = transaction.card_type,
                last_four = transaction.last_four,
                amount = transaction.amount,
                decline_reason = transaction.decline_reason,
                decline_code = transaction.decline_code,
                js_sequence = ack.Seq,
                js_stream = ack.Stream,
                size_bytes = bytes.Length
            });
        }

        static void LogInfo(string message, object? data = null)
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = "INFO",
                logger = "payment-publisher-jetstream",
                message = message,
                module = "Program",
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
                logger = "payment-publisher-jetstream",
                message = message,
                module = "Program",
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
                logger = "payment-publisher-jetstream",
                message = message,
                module = "Program",
                hostname = _hostname,
                error = ex.Message,
                stacktrace = ex.StackTrace
            };
            Console.WriteLine(JsonSerializer.Serialize(logEntry));
        }
    }
}
