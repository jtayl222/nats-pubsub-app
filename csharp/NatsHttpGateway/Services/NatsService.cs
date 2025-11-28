using System.Text;
using System.Text.Json;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NatsHttpGateway.Models;

namespace NatsHttpGateway.Services;

public class NatsService : IDisposable
{
    private readonly NatsConnection _nats;
    private readonly INatsJSContext _js;
    private readonly ILogger<NatsService> _logger;
    private readonly string _defaultStreamPrefix;
    private readonly Dictionary<string, string> _subjectToStreamMap = new();

    public NatsService(IConfiguration configuration, ILogger<NatsService> logger)
    {
        _logger = logger;
        var natsUrl = configuration["NATS_URL"] ?? "nats://localhost:4222";
        _defaultStreamPrefix = configuration["STREAM_PREFIX"] ?? "EVENTS";

        var opts = new NatsOpts { Url = natsUrl };
        _nats = new NatsConnection(opts);
        _nats.ConnectAsync().AsTask().Wait();
        _js = new NatsJSContext(_nats);

        _logger.LogInformation("NATS Gateway connected to {NatsUrl}", natsUrl);
    }

    public bool IsConnected => _nats.ConnectionState == NatsConnectionState.Open;

    public bool IsJetStreamAvailable => _nats.ServerInfo?.JetStreamAvailable ?? false;

    public string NatsUrl => _nats.Opts.Url;

    /// <summary>
    /// Publishes a message to the specified subject via JetStream
    /// </summary>
    public async Task<PublishResponse> PublishAsync(string subject, PublishRequest request)
    {
        try
        {
            // Ensure stream exists for this subject
            var streamName = await EnsureStreamExistsAsync(subject);

            // Build message payload
            var payload = new
            {
                message_id = request.MessageId ?? Guid.NewGuid().ToString(),
                timestamp = DateTime.UtcNow.ToString("o"),
                source = request.Source ?? "http-gateway",
                data = request.Data
            };

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Publish via JetStream
            var ack = await _js.PublishAsync(subject, bytes);

            _logger.LogInformation("Published message to {Subject} in stream {Stream}, seq={Sequence}",
                subject, ack.Stream, ack.Seq);

            return new PublishResponse
            {
                Published = true,
                Subject = subject,
                Stream = ack.Stream,
                Sequence = ack.Seq,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to {Subject}", subject);
            throw;
        }
    }

    /// <summary>
    /// Fetches the last N messages from a subject (stateless)
    /// </summary>
    public async Task<FetchMessagesResponse> FetchMessagesAsync(string subject, int limit = 10)
    {
        try
        {
            // Get or create stream for this subject
            var streamName = await EnsureStreamExistsAsync(subject);

            // Get stream info to find the last sequence number
            var streamInfo = await _js.GetStreamAsync(streamName);
            var lastSeq = streamInfo.Info.State.LastSeq;
            var totalMessages = streamInfo.Info.State.Messages;

            // Calculate start sequence to get last N messages
            // If stream has fewer messages than limit, start from beginning
            var startSeq = (ulong)Math.Max(1, (long)lastSeq - limit + 1);

            _logger.LogInformation("Stream {Stream} has {TotalMessages} messages (seq {FirstSeq}-{LastSeq}), fetching from seq {StartSeq}",
                streamName, totalMessages, streamInfo.Info.State.FirstSeq, lastSeq, startSeq);

            // Create ephemeral consumer starting from calculated sequence
            var consumerConfig = new ConsumerConfig
            {
                Name = $"http-fetch-{Guid.NewGuid()}",
                DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
                OptStartSeq = startSeq,
                FilterSubject = subject,
                AckPolicy = ConsumerConfigAckPolicy.None,
                InactiveThreshold = TimeSpan.FromSeconds(5)
            };

            var consumer = await _js.CreateConsumerAsync(streamName, consumerConfig);

            var messages = new List<MessageResponse>();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    opts: new NatsJSFetchOpts { MaxMsgs = limit, Expires = TimeSpan.FromSeconds(5) },
                    cancellationToken: cts.Token))
                {
                    if (msg.Data != null)
                    {
                        var json = Encoding.UTF8.GetString(msg.Data);
                        var data = JsonSerializer.Deserialize<object>(json);

                        messages.Add(new MessageResponse
                        {
                            Subject = msg.Subject,
                            Sequence = msg.Metadata?.Sequence.Stream,
                            Timestamp = msg.Metadata?.Timestamp.DateTime,
                            Data = data,
                            SizeBytes = msg.Data.Length
                        });
                    }

                    if (messages.Count >= limit)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout is expected if fewer than limit messages exist
            }

            // Cleanup ephemeral consumer
            try
            {
                await _js.DeleteConsumerAsync(streamName, consumerConfig.Name);
            }
            catch
            {
                // Ignore cleanup errors
            }

            _logger.LogInformation("Fetched {Count} messages from {Subject}", messages.Count, subject);

            return new FetchMessagesResponse
            {
                Subject = subject,
                Count = messages.Count,
                Messages = messages,
                Stream = streamName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages from {Subject}", subject);
            throw;
        }
    }

    /// <summary>
    /// Ensures a JetStream stream exists for the given subject
    /// </summary>
    private async Task<string> EnsureStreamExistsAsync(string subject)
    {
        // Check cache first
        if (_subjectToStreamMap.TryGetValue(subject, out var cachedStream))
        {
            return cachedStream;
        }

        // Determine stream name from subject
        // e.g., "events.test" -> "EVENTS", "payments.approved" -> "PAYMENTS"
        var streamName = DetermineStreamName(subject);

        try
        {
            // Try to get existing stream
            await _js.GetStreamAsync(streamName);
            _subjectToStreamMap[subject] = streamName;
            return streamName;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Stream doesn't exist, create it
            _logger.LogInformation("Creating stream {Stream} for subject pattern {Subject}", streamName, subject);

            var subjectPattern = GetSubjectPattern(subject);
            var streamConfig = new StreamConfig(streamName, new[] { subjectPattern })
            {
                Description = $"Auto-created stream for {subjectPattern}",
                Retention = StreamConfigRetention.Limits,
                MaxMsgs = 10000,
                MaxBytes = 1024 * 1024 * 100, // 100MB
                MaxAge = TimeSpan.FromHours(24),
                Storage = StreamConfigStorage.File,
                NumReplicas = 1
            };

            await _js.CreateStreamAsync(streamConfig);
            _subjectToStreamMap[subject] = streamName;

            _logger.LogInformation("Stream {Stream} created successfully", streamName);
            return streamName;
        }
    }

    /// <summary>
    /// Determines stream name from subject (e.g., "events.test" -> "EVENTS")
    /// </summary>
    private string DetermineStreamName(string subject)
    {
        var parts = subject.Split('.');
        return parts.Length > 0 ? parts[0].ToUpperInvariant() : _defaultStreamPrefix;
    }

    /// <summary>
    /// Converts specific subject to wildcard pattern (e.g., "events.test" -> "events.>")
    /// </summary>
    private string GetSubjectPattern(string subject)
    {
        var parts = subject.Split('.');
        return parts.Length > 0 ? $"{parts[0]}.>" : ">";
    }

    public void Dispose()
    {
        _nats?.DisposeAsync().AsTask().Wait();
    }
}
