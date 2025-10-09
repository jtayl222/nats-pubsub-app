using System;
using System.Text;
using System.Text.Json;
using NATS.Client;

namespace PaymentPublisher
{
    class Program
    {
        private static IConnection? _connection;
        private static readonly string _natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        private static readonly string _hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? "payment-publisher";
        private static readonly double _publishInterval = double.Parse(Environment.GetEnvironmentVariable("PUBLISH_INTERVAL") ?? "5.0");

        private static int _transactionCount = 0;
        private static int _acceptedCount = 0;
        private static int _declinedCount = 0;
        private static readonly DateTime _startTime = DateTime.UtcNow;
        private static DateTime _lastErrorTime = DateTime.UtcNow;
        private static readonly Random _random = new Random();

        static async Task Main(string[] args)
        {
            LogInfo("Starting Payment Publisher", new
            {
                nats_url = _natsUrl,
                hostname = _hostname,
                publish_interval = _publishInterval
            });

            try
            {
                ConnectToNats();
                await PublishLoop();
            }
            catch (Exception ex)
            {
                LogError("Fatal error in payment publisher", ex);
                Environment.Exit(1);
            }
        }

        static void ConnectToNats()
        {
            var options = ConnectionFactory.GetDefaultOptions();
            options.Url = _natsUrl;
            options.Name = $"payment-publisher-{_hostname}";
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

        static async Task PublishLoop()
        {
            LogInfo("Starting payment transaction loop", new
            {
                interval_seconds = _publishInterval,
                error_frequency = "~1 per minute"
            });

            while (true)
            {
                try
                {
                    if (_connection == null || !_connection.State.Equals(ConnState.CONNECTED))
                    {
                        LogWarning("Not connected to NATS, waiting...");
                        await Task.Delay(1000);
                        continue;
                    }

                    _transactionCount++;

                    // Determine if this should be a declined transaction
                    // Roughly once per minute (60 seconds / publish_interval = number of messages per minute)
                    var messagesPerMinute = (int)(60.0 / _publishInterval);
                    var timeSinceLastError = (DateTime.UtcNow - _lastErrorTime).TotalSeconds;

                    // Random chance for decline, but at least once per minute
                    bool shouldDecline = timeSinceLastError >= 60 || _random.Next(messagesPerMinute) == 0;

                    if (shouldDecline)
                    {
                        await PublishDeclinedTransaction();
                        _lastErrorTime = DateTime.UtcNow;
                    }
                    else
                    {
                        await PublishAcceptedTransaction();
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
                catch (NATSConnectionException)
                {
                    LogWarning("NATS connection issue, will retry...");
                    await Task.Delay(5000);
                }
                catch (Exception ex)
                {
                    LogError("Publish error", ex);
                    await Task.Delay(5000);
                }
            }
        }

        static async Task PublishAcceptedTransaction()
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

            _connection?.Publish("payments.credit_card.accepted", bytes);

            LogInfo("Credit card accepted", new
            {
                transaction_id = transaction.transaction_id,
                subject = "payments.credit_card.accepted",
                card_type = transaction.card_type,
                last_four = transaction.last_four,
                amount = transaction.amount,
                authorization_code = transaction.authorization_code,
                size_bytes = bytes.Length
            });

            await Task.CompletedTask;
        }

        static async Task PublishDeclinedTransaction()
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

            _connection?.Publish("payments.credit_card.declined", bytes);

            // Log as ERROR level so it shows up prominently in Loki
            LogError("Credit card declined", new
            {
                transaction_id = transaction.transaction_id,
                subject = "payments.credit_card.declined",
                card_type = transaction.card_type,
                last_four = transaction.last_four,
                amount = transaction.amount,
                decline_reason = transaction.decline_reason,
                decline_code = transaction.decline_code,
                size_bytes = bytes.Length
            });

            await Task.CompletedTask;
        }

        static void LogInfo(string message, object? data = null)
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = "INFO",
                logger = "payment-publisher",
                message = message,
                module = "Program",
                function = "PublishLoop",
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
                logger = "payment-publisher",
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
                logger = "payment-publisher",
                message = message,
                module = "Program",
                hostname = _hostname,
                data = data
            };
            Console.WriteLine(JsonSerializer.Serialize(logEntry));
        }
    }
}
