using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NatsHttpGateway.Models;

namespace NatsHttpGateway.Services;

public class NatsService : INatsService, IDisposable
{
    private readonly NatsConnection _nats;
    private readonly INatsJSContext _js;
    private readonly ILogger<NatsService> _logger;
    private readonly string _defaultStreamPrefix;
    private readonly Dictionary<string, string> _subjectToStreamMap = new();
    private bool _disposed = false;

    public NatsService(IConfiguration configuration, ILogger<NatsService> logger)
    {
        _logger = logger;
        var natsUrl = configuration["NATS_URL"] ?? "nats://localhost:4222";
        _defaultStreamPrefix = configuration["STREAM_PREFIX"] ?? "events";

        try
        {
            var opts = new NatsOpts { Url = natsUrl };
            _nats = new NatsConnection(opts);
            _nats.ConnectAsync().AsTask().Wait();
            _js = new NatsJSContext(_nats);

            _logger.LogInformation("NATS Gateway connected to {NatsUrl}", natsUrl);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize NATS connection to {natsUrl}", ex);
        }
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
            await EnsureStreamExistsAsync(subject);

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
                Stream = ack.Stream!,
                Sequence = ack.Seq,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to publish message to subject '{subject}'", ex);
        }
    }

    /// <summary>
    /// Fetches the last N messages from a subject using an ephemeral consumer (stateless)
    /// </summary>
    public async Task<FetchMessagesResponse> FetchMessagesAsync(string subjectFilter, int limit = 10, int timeoutSeconds = 5)
    {
        try
        {
            // Get or create stream for this subject filter
            var streamName = await EnsureStreamExistsAsync(subjectFilter);

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
                FilterSubject = subjectFilter,
                AckPolicy = ConsumerConfigAckPolicy.None,
                InactiveThreshold = TimeSpan.FromSeconds(5)
            };

            var consumer = await _js.CreateConsumerAsync(streamName, consumerConfig);

            var messages = new List<MessageResponse>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    opts: new NatsJSFetchOpts { MaxMsgs = limit, Expires = TimeSpan.FromSeconds(timeoutSeconds) },
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

            _logger.LogInformation("Fetched {Count} messages from {SubjectFilter}", messages.Count, subjectFilter);

            return new FetchMessagesResponse
            {
                Subject = subjectFilter,
                Count = messages.Count,
                Messages = messages,
                Stream = streamName
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to fetch messages from subject filter '{subjectFilter}'", ex);
        }
    }

    /// <summary>
    /// Fetches messages from a stream using a durable (well-known) consumer
    /// </summary>
    public async Task<FetchMessagesResponse> FetchMessagesFromConsumerAsync(string streamName, string consumerName, int limit = 10, int timeoutSeconds = 5)
    {
        try
        {
            _logger.LogInformation("Fetching {Limit} messages from stream {Stream} using consumer {ConsumerName}",
                limit, streamName, consumerName);

            // Get the existing durable consumer
            INatsJSConsumer consumer;
            try
            {
                consumer = await _js.GetConsumerAsync(streamName, consumerName);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                throw new InvalidOperationException(
                    $"Consumer '{consumerName}' does not exist in stream '{streamName}'. " +
                    $"Please create the consumer first using the NATS CLI or management API.", ex);
            }

            var messages = new List<MessageResponse>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    opts: new NatsJSFetchOpts { MaxMsgs = limit, Expires = TimeSpan.FromSeconds(timeoutSeconds) },
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

                        // Acknowledge the message to advance consumer position
                        await msg.AckAsync();
                    }

                    if (messages.Count >= limit)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout is expected if fewer than limit messages exist
            }

            _logger.LogInformation("Fetched {Count} messages from consumer {ConsumerName} in stream {Stream}",
                messages.Count, consumerName, streamName);

            return new FetchMessagesResponse
            {
                Subject = string.Empty, // Subject filtering is configured in the consumer
                Count = messages.Count,
                Messages = messages,
                Stream = streamName
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to fetch messages from consumer '{consumerName}' in stream '{streamName}'", ex);
        }
    }

    /// <summary>
    /// Streams messages from a subject using an ephemeral consumer (for WebSocket)
    /// </summary>
    public async IAsyncEnumerable<MessageResponse> StreamMessagesAsync(
        string subjectFilter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        INatsJSConsumer? consumer = null;
        string? streamName = null;
        string? consumerName = null;

        try
        {
            // Get or create stream for this subject filter
            streamName = await EnsureStreamExistsAsync(subjectFilter);

            // Create ephemeral consumer for streaming
            consumerName = $"ws-stream-{Guid.NewGuid()}";
            var consumerConfig = new ConsumerConfig
            {
                Name = consumerName,
                DeliverPolicy = ConsumerConfigDeliverPolicy.New, // Only new messages
                FilterSubject = subjectFilter,
                AckPolicy = ConsumerConfigAckPolicy.None,
                InactiveThreshold = TimeSpan.FromMinutes(5)
            };

            consumer = await _js.CreateConsumerAsync(streamName, consumerConfig);

            _logger.LogInformation("Started streaming from {SubjectFilter} using consumer {ConsumerName}",
                subjectFilter, consumerName);

            // Stream messages continuously
            await foreach (var msg in consumer.ConsumeAsync<byte[]>(
                opts: new NatsJSConsumeOpts(),
                cancellationToken: cancellationToken))
            {
                if (msg.Data != null)
                {
                    var json = Encoding.UTF8.GetString(msg.Data);
                    var data = JsonSerializer.Deserialize<object>(json);

                    yield return new MessageResponse
                    {
                        Subject = msg.Subject,
                        Sequence = msg.Metadata?.Sequence.Stream,
                        Timestamp = msg.Metadata?.Timestamp.DateTime,
                        Data = data,
                        SizeBytes = msg.Data.Length
                    };
                }
            }
        }
        finally
        {
            // Cleanup ephemeral consumer on disconnect
            if (streamName != null && consumerName != null)
            {
                try
                {
                    await _js.DeleteConsumerAsync(streamName, consumerName);
                    _logger.LogInformation("Deleted ephemeral consumer {ConsumerName}", consumerName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup consumer {ConsumerName}", consumerName);
                }
            }
        }
    }

    /// <summary>
    /// Streams messages from a durable consumer (for WebSocket)
    /// </summary>
    public async IAsyncEnumerable<MessageResponse> StreamMessagesFromConsumerAsync(
        string streamName,
        string consumerName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Get the existing durable consumer
        INatsJSConsumer consumer;
        try
        {
            consumer = await _js.GetConsumerAsync(streamName, consumerName);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            throw new InvalidOperationException(
                $"Consumer '{consumerName}' does not exist in stream '{streamName}'. " +
                $"Please create the consumer first using the NATS CLI or management API.", ex);
        }

        _logger.LogInformation("Started streaming from consumer {ConsumerName} in stream {StreamName}",
            consumerName, streamName);

        // Stream messages continuously from durable consumer
        await foreach (var msg in consumer.ConsumeAsync<byte[]>(
            opts: new NatsJSConsumeOpts(),
            cancellationToken: cancellationToken))
        {
            if (msg.Data != null)
            {
                var json = Encoding.UTF8.GetString(msg.Data);
                var data = JsonSerializer.Deserialize<object>(json);

                yield return new MessageResponse
                {
                    Subject = msg.Subject,
                    Sequence = msg.Metadata?.Sequence.Stream,
                    Timestamp = msg.Metadata?.Timestamp.DateTime,
                    Data = data,
                    SizeBytes = msg.Data.Length
                };

                // Acknowledge the message to advance consumer position
                await msg.AckAsync();
            }
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
            _logger.LogInformation(ex, "Creating stream {Stream} for subject pattern {Subject}", streamName, subject);

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
    /// Determines stream name from subject (e.g., "events.test" -> "events") while
    /// preserving the caller's original casing.
    /// </summary>
    private string DetermineStreamName(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return _defaultStreamPrefix;
        }

        var parts = subject.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : _defaultStreamPrefix;
    }

    /// <summary>
    /// Converts specific subject to wildcard pattern (e.g., "events.test" -> "events.>")
    /// </summary>
    private static string GetSubjectPattern(string subject)
    {
        var parts = subject.Split('.');
        return parts.Length > 0 ? $"{parts[0]}.>" : ">";
    }

    /// <summary>
    /// Lists all JetStream streams
    /// </summary>
    public async Task<List<StreamSummary>> ListStreamsAsync()
    {
        try
        {
            var streams = new List<StreamSummary>();

            await foreach (var stream in _js.ListStreamsAsync())
            {
                streams.Add(new StreamSummary
                {
                    Name = stream.Info.Config.Name!,
                    Subjects = stream.Info.Config.Subjects?.ToArray() ?? Array.Empty<string>(),
                    Messages = (ulong)stream.Info.State.Messages,
                    Bytes = (ulong)stream.Info.State.Bytes,
                    FirstSeq = stream.Info.State.FirstSeq,
                    LastSeq = stream.Info.State.LastSeq,
                    Consumers = (int)stream.Info.State.ConsumerCount
                });
            }

            _logger.LogInformation("Listed {Count} JetStream streams", streams.Count);
            return streams;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to list JetStream streams", ex);
        }
    }

    /// <summary>
    /// Gets detailed information about a specific stream
    /// </summary>
    public async Task<StreamSummary> GetStreamInfoAsync(string name)
    {
        try
        {
            var stream = await _js.GetStreamAsync(name);

            _logger.LogInformation("Retrieved info for stream {Stream}", name);

            return new StreamSummary
            {
                Name = stream.Info.Config.Name!,
                Subjects = stream.Info.Config.Subjects?.ToArray() ?? Array.Empty<string>(),
                Messages = (ulong)stream.Info.State.Messages,
                Bytes = (ulong)stream.Info.State.Bytes,
                FirstSeq = stream.Info.State.FirstSeq,
                LastSeq = stream.Info.State.LastSeq,
                Consumers = (int)stream.Info.State.ConsumerCount,

                Configuration = new StreamConfigurationInfo
                {
                    Description = stream.Info.Config.Description,
                    Replicas = stream.Info.Config.NumReplicas,
                    Storage = stream.Info.Config.Storage.ToString(),
                    Retention = stream.Info.Config.Retention.ToString(),
                    Discard = stream.Info.Config.Discard.ToString(),
                    NoAck = stream.Info.Config.NoAck,
                    MaxAge = stream.Info.Config.MaxAge,
                    MaxBytes = stream.Info.Config.MaxBytes,
                    MaxMsgSize = stream.Info.Config.MaxMsgSize,
                    MaxMsgs = stream.Info.Config.MaxMsgs,
                    MaxConsumers = stream.Info.Config.MaxConsumers,
                    MaxMsgsPerSubject = stream.Info.Config.MaxMsgsPerSubject,
                    DuplicateWindow = stream.Info.Config.DuplicateWindow,
                    AllowRollup = stream.Info.Config.AllowRollupHdrs,
                    DenyDelete = stream.Info.Config.DenyDelete,
                    DenyPurge = stream.Info.Config.DenyPurge,
                    Sealed = stream.Info.Config.Sealed
                },

                State = new StreamStateInfo
                {
                    Messages = (ulong)stream.Info.State.Messages,
                    Bytes = (ulong)stream.Info.State.Bytes,
                    FirstSeq = stream.Info.State.FirstSeq,
                    LastSeq = stream.Info.State.LastSeq,
                    FirstTime = stream.Info.State.FirstTs,
                    LastTime = stream.Info.State.LastTs,
                    ConsumerCount = (int)stream.Info.State.ConsumerCount,
                    NumSubjects = stream.Info.State.NumSubjects,
                    NumDeleted = stream.Info.State.NumDeleted
                }
            };
        }
        catch (Exception ex)
        {
            throw new KeyNotFoundException($"Failed to get stream info for '{name}'", ex);
        }
    }

    /// <summary>
    /// Gets all distinct subjects with messages in a stream
    /// </summary>
    public async Task<StreamSubjectsResponse> GetStreamSubjectsAsync(string name)
    {
        try
        {
            // Get stream info with subject details - need to request with subject filter
            var request = new StreamInfoRequest { SubjectsFilter = ">" };
            var stream = await _js.GetStreamAsync(name, request);

            var subjects = new List<SubjectDetail>();
            string? note = null;

            // Check if stream has subject details
            if (stream.Info.State.Subjects != null && stream.Info.State.Subjects.Count > 0)
            {
                foreach (var kvp in stream.Info.State.Subjects)
                {
                    subjects.Add(new SubjectDetail
                    {
                        Subject = kvp.Key,
                        Messages = (ulong)kvp.Value
                    });
                }
            }
            else
            {
                note = "No subject-level statistics available for this stream";
            }

            _logger.LogInformation("Retrieved {Count} subjects from stream {Stream}", subjects.Count, name);

            return new StreamSubjectsResponse
            {
                StreamName = name,
                Count = subjects.Count,
                Subjects = subjects.OrderByDescending(s => s.Messages).ToList(),
                Note = note
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get subjects for stream '{name}'", ex);
        }
    }

    /// <summary>
    /// Creates a new consumer on a stream
    /// </summary>
    public async Task<ConsumerDetails> CreateConsumerAsync(string streamName, CreateConsumerRequest request)
    {
        try
        {
            // Map string values to enums
            var deliverPolicy = request.DeliverPolicy.ToLower() switch
            {
                "all" => ConsumerConfigDeliverPolicy.All,
                "last" => ConsumerConfigDeliverPolicy.Last,
                "new" => ConsumerConfigDeliverPolicy.New,
                "by_start_sequence" => ConsumerConfigDeliverPolicy.ByStartSequence,
                "by_start_time" => ConsumerConfigDeliverPolicy.ByStartTime,
                _ => ConsumerConfigDeliverPolicy.All
            };

            var ackPolicy = request.AckPolicy.ToLower() switch
            {
                "none" => ConsumerConfigAckPolicy.None,
                "all" => ConsumerConfigAckPolicy.All,
                "explicit" => ConsumerConfigAckPolicy.Explicit,
                _ => ConsumerConfigAckPolicy.Explicit
            };

            // Determine inactive threshold
            TimeSpan inactiveThreshold;
            if (request.InactiveThreshold.HasValue)
            {
                inactiveThreshold = request.InactiveThreshold.Value;
            }
            else if (request.Durable)
            {
                // Durable consumers should persist - use very long threshold (effectively no auto-delete)
                // NATS doesn't have "infinity", so we use 365 days as a practical maximum
                inactiveThreshold = TimeSpan.FromDays(365);
            }
            else
            {
                // Ephemeral consumers auto-delete after 5 minutes of inactivity
                inactiveThreshold = TimeSpan.FromMinutes(5);
            }

            // For durable consumers, use the provided name. For ephemeral, name can be empty (NATS generates one)
            string? consumerName;
            if (request.Durable)
            {
                consumerName = request.Name;
            }
            else
            {
                consumerName = string.IsNullOrEmpty(request.Name) ? null : request.Name;
            }

            var consumerConfig = new NATS.Client.JetStream.Models.ConsumerConfig
            {
                Name = consumerName,
                Description = request.Description,
                FilterSubject = request.FilterSubject,
                DeliverPolicy = deliverPolicy,
                AckPolicy = ackPolicy,
                AckWait = request.AckWait ?? default,
                MaxDeliver = request.MaxDeliver ?? -1,
                InactiveThreshold = inactiveThreshold,
                MaxAckPending = request.MaxAckPending ?? -1,
                FlowControl = request.FlowControl ?? false,
                IdleHeartbeat = request.IdleHeartbeat ?? default
            };

            // Set optional start sequence or time
            if (request.StartSequence.HasValue)
            {
                consumerConfig.OptStartSeq = request.StartSequence.Value;
            }
            if (request.StartTime.HasValue)
            {
                consumerConfig.OptStartTime = request.StartTime.Value;
            }

            var consumer = await _js.CreateConsumerAsync(streamName, consumerConfig);

            _logger.LogInformation("Created consumer {ConsumerName} on stream {StreamName}", request.Name, streamName);

            return await MapConsumerToInfo(consumer, streamName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create consumer '{request.Name}' on stream '{streamName}'", ex);
        }
    }

    /// <summary>
    /// Lists all consumers for a stream
    /// </summary>
    public async Task<ConsumerListResult> ListConsumersAsync(string streamName)
    {
        try
        {
            var consumers = new List<ConsumerSummary>();

            await foreach (var consumer in _js.ListConsumersAsync(streamName))
            {
                consumers.Add(new ConsumerSummary
                {
                    StreamName = streamName,
                    Name = consumer.Info.Config.Name!,
                    Description = consumer.Info.Config.Description,
                    Created = consumer.Info.Created.DateTime,
                    Config = MapConsumerConfig(consumer.Info.Config),
                    State = MapConsumerState(consumer.Info)
                });
            }

            _logger.LogInformation("Listed {Count} consumers for stream {StreamName}", consumers.Count, streamName);

            return new ConsumerListResult
            {
                StreamName = streamName,
                Count = consumers.Count,
                Consumers = consumers
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to list consumers for stream '{streamName}'", ex);
        }
    }

    /// <summary>
    /// Gets detailed information about a specific consumer
    /// </summary>
    public async Task<ConsumerDetails> GetConsumerInfoAsync(string streamName, string consumerName)
    {
        try
        {
            var consumer = await _js.GetConsumerAsync(streamName, consumerName);

            _logger.LogInformation("Retrieved info for consumer {ConsumerName} on stream {StreamName}", consumerName, streamName);

            return await MapConsumerToInfo(consumer, streamName);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            throw new KeyNotFoundException($"Consumer '{consumerName}' not found on stream '{streamName}'");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get consumer info for '{consumerName}' on stream '{streamName}'", ex);
        }
    }

    /// <summary>
    /// Deletes a consumer from a stream
    /// </summary>
    public async Task<ConsumerDeleteResult> DeleteConsumerAsync(string streamName, string consumerName)
    {
        try
        {
            await _js.DeleteConsumerAsync(streamName, consumerName);

            _logger.LogInformation("Deleted consumer {ConsumerName} from stream {StreamName}", consumerName, streamName);

            return new ConsumerDeleteResult
            {
                Success = true,
                Message = $"Consumer '{consumerName}' deleted successfully from stream '{streamName}'"
            };
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            throw new KeyNotFoundException($"Consumer '{consumerName}' not found on stream '{streamName}'");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to delete consumer '{consumerName}' from stream '{streamName}'", ex);
        }
    }

    /// <summary>
    /// Gets health status of a consumer
    /// </summary>
    public async Task<ConsumerHealthResponse> GetConsumerHealthAsync(string streamName, string consumerName)
    {
        try
        {
            var consumer = await _js.GetConsumerAsync(streamName, consumerName);
            var info = consumer.Info;

            var lastActivity = info.Delivered.LastActive;
            var timeSinceActivity = DateTimeOffset.UtcNow - lastActivity;

            // Determine health status
            var isHealthy = true;
            var status = "Healthy";
            string? issue = null;

            // Check if consumer has been inactive too long
            if (info.Config.InactiveThreshold != default && timeSinceActivity > info.Config.InactiveThreshold)
            {
                isHealthy = false;
                status = "Inactive";
                issue = $"Consumer has been inactive for {timeSinceActivity:hh\\:mm\\:ss}";
            }

            // Check if too many pending acks
            if (info.NumAckPending > 1000)
            {
                isHealthy = false;
                status = "Overloaded";
                issue = $"High pending acknowledgments: {info.NumAckPending}";
            }

            // Check if messages are piling up
            var pendingMessages = info.NumPending;
            if (pendingMessages > 10000 && isHealthy) // Don't override existing issues
            {
                isHealthy = false;
                status = "Lagging";
                issue = $"High pending messages: {pendingMessages}";
            }

            _logger.LogInformation("Health check for consumer {ConsumerName}: {Status}", consumerName, status);

            return new ConsumerHealthResponse
            {
                ConsumerName = consumerName,
                StreamName = streamName,
                IsHealthy = isHealthy,
                Status = status,
                LastActivity = lastActivity.DateTime,
                TimeSinceLastActivity = timeSinceActivity,
                PendingMessages = (long)pendingMessages,
                AckPending = (long)info.NumAckPending,
                Issue = issue
            };
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            throw new KeyNotFoundException($"Consumer '{consumerName}' not found on stream '{streamName}'");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to check health for consumer '{consumerName}'", ex);
        }
    }

    /// <summary>
    /// Maps NATS consumer to ConsumerDetails with metrics
    /// </summary>
    private async Task<ConsumerDetails> MapConsumerToInfo(INatsJSConsumer consumer, string streamName)
    {
        var info = consumer.Info;

        // Get stream info to calculate lag
        var stream = await _js.GetStreamAsync(streamName);
        var streamLastSeq = stream.Info.State.LastSeq;
        var consumerLastSeq = info.Delivered.StreamSeq;
        var consumerLag = (long)(streamLastSeq - consumerLastSeq);

        // Calculate acknowledged messages (delivered - ack pending)
        var acknowledged = (long)info.Delivered.ConsumerSeq - (long)info.NumAckPending;

        return new ConsumerDetails
        {
            StreamName = streamName,
            Name = info.Config.Name!,
            Description = info.Config.Description,
            Created = info.Created.DateTime,
            Config = MapConsumerConfig(info.Config),
            State = MapConsumerState(info),
            Metrics = new ConsumerMetrics
            {
                ConsumerLag = consumerLag,
                PendingMessages = (long)info.NumPending,
                AcknowledgedMessages = acknowledged > 0 ? acknowledged : 0,
                RedeliveredMessages = (long)info.NumRedelivered,
                AverageAckTime = 0, // Would need to track this separately
                IsHealthy = consumerLag < 1000 && (long)info.NumAckPending < 100,
                HealthStatus = consumerLag > 1000 ? "Lagging" : "Healthy"
            }
        };
    }

    /// <summary>
    /// Maps NATS consumer config to our model
    /// </summary>
    private ConsumerConfiguration MapConsumerConfig(NATS.Client.JetStream.Models.ConsumerConfig config)
    {
        return new ConsumerConfiguration
        {
            FilterSubject = config.FilterSubject,
            DeliverPolicy = config.DeliverPolicy.ToString(),
            AckPolicy = config.AckPolicy.ToString(),
            AckWait = config.AckWait == default ? null : config.AckWait,
            MaxDeliver = (int)config.MaxDeliver,
            InactiveThreshold = config.InactiveThreshold == default ? null : config.InactiveThreshold,
            MaxAckPending = (int)config.MaxAckPending,
            FlowControl = config.FlowControl,
            IdleHeartbeat = config.IdleHeartbeat == default ? null : config.IdleHeartbeat,
            OptStartSeq = config.OptStartSeq == 0 ? null : config.OptStartSeq,
            OptStartTime = config.OptStartTime == default ? null : (DateTime?)config.OptStartTime.DateTime
        };
    }

    /// <summary>
    /// Maps NATS consumer info to consumer state
    /// </summary>
    private ConsumerStateData MapConsumerState(NATS.Client.JetStream.Models.ConsumerInfo info)
    {
        return new ConsumerStateData
        {
            Delivered = info.Delivered.ConsumerSeq,
            AckPending = (ulong)info.NumAckPending,
            Redelivered = (ulong)info.NumRedelivered,
            NumPending = (long)info.NumPending,
            NumWaiting = info.NumWaiting,
            LastDelivered = info.Delivered.LastActive.DateTime
        };
    }

    /// <summary>
    /// Peeks at messages from a consumer without acknowledging them
    /// </summary>
    public async Task<ConsumerPeekMessagesResponse> PeekConsumerMessagesAsync(string streamName, string consumerName, int limit = 10)
    {
        try
        {
            var consumer = await _js.GetConsumerAsync(streamName, consumerName);
            var messages = new List<MessagePreview>();

            // Fetch messages without acknowledging
            await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = limit }))
            {
                var dataPreview = msg.Data != null && msg.Data.Length > 0
                    ? TryGetStringPreview(msg.Data, 100)
                    : null;

                messages.Add(new MessagePreview
                {
                    Sequence = msg.Metadata?.Sequence.Stream ?? 0,
                    Subject = msg.Subject,
                    Timestamp = msg.Metadata?.Timestamp.DateTime,
                    SizeBytes = msg.Data?.Length ?? 0,
                    DataPreview = dataPreview
                });

                if (messages.Count >= limit)
                    break;
            }

            _logger.LogInformation("Peeked {Count} messages from consumer {ConsumerName}", messages.Count, consumerName);

            return new ConsumerPeekMessagesResponse
            {
                ConsumerName = consumerName,
                StreamName = streamName,
                Count = messages.Count,
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to peek messages from consumer '{consumerName}'", ex);
        }
    }

    /// <summary>
    /// Resets a consumer to replay messages
    /// </summary>
    public async Task<ConsumerResetResponse> ResetConsumerAsync(string streamName, string consumerName, ConsumerResetRequest request)
    {
        try
        {
            // Get current consumer config
            var existingConsumer = await _js.GetConsumerAsync(streamName, consumerName);
            var config = existingConsumer.Info.Config;

            // Delete and recreate with new starting point
            await _js.DeleteConsumerAsync(streamName, consumerName);

            var newConfig = new NATS.Client.JetStream.Models.ConsumerConfig
            {
                Name = config.Name,
                Description = config.Description,
                FilterSubject = config.FilterSubject,
                DeliverPolicy = request.Action.ToLower() switch
                {
                    "reset" => ConsumerConfigDeliverPolicy.All,
                    "replay_from_sequence" => ConsumerConfigDeliverPolicy.ByStartSequence,
                    "replay_from_time" => ConsumerConfigDeliverPolicy.ByStartTime,
                    _ => ConsumerConfigDeliverPolicy.All
                },
                AckPolicy = config.AckPolicy,
                AckWait = config.AckWait,
                MaxDeliver = config.MaxDeliver,
                InactiveThreshold = config.InactiveThreshold,
                MaxAckPending = config.MaxAckPending,
                FlowControl = config.FlowControl,
                IdleHeartbeat = config.IdleHeartbeat
            };

            if (request.Sequence.HasValue)
            {
                newConfig.OptStartSeq = request.Sequence.Value;
            }
            if (request.Time.HasValue)
            {
                newConfig.OptStartTime = request.Time.Value;
            }

            await _js.CreateConsumerAsync(streamName, newConfig);

            _logger.LogInformation("Reset consumer {ConsumerName} with action {Action}", consumerName, request.Action);

            return new ConsumerResetResponse
            {
                Success = true,
                Message = $"Consumer '{consumerName}' reset successfully",
                ConsumerName = consumerName
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to reset consumer '{consumerName}'", ex);
        }
    }

    /// <summary>
    /// Gets metrics history for a consumer (simulated with current snapshot)
    /// </summary>
    public async Task<ConsumerMetricsHistoryResponse> GetConsumerMetricsHistoryAsync(string streamName, string consumerName, int samples = 10)
    {
        try
        {
            // In a real implementation, you'd store metrics in a time-series database
            // For now, we'll just return current metrics as a single snapshot
            var consumer = await _js.GetConsumerAsync(streamName, consumerName);
            var info = consumer.Info;

            var stream = await _js.GetStreamAsync(streamName);
            var consumerLag = (long)(stream.Info.State.LastSeq - info.Delivered.StreamSeq);
            var acknowledged = (long)info.Delivered.ConsumerSeq - (long)info.NumAckPending;

            var now = DateTime.UtcNow;
            var history = new List<ConsumerMetricsSnapshot>
            {
                new ConsumerMetricsSnapshot
                {
                    Timestamp = now,
                    ConsumerLag = consumerLag,
                    PendingMessages = (long)info.NumPending,
                    AcknowledgedMessages = acknowledged > 0 ? acknowledged : 0,
                    RedeliveredMessages = (long)info.NumRedelivered,
                    DeliveredCount = info.Delivered.ConsumerSeq,
                    IsHealthy = consumerLag < 1000 && (long)info.NumAckPending < 100
                }
            };

            _logger.LogInformation("Retrieved metrics history for consumer {ConsumerName}", consumerName);

            return new ConsumerMetricsHistoryResponse
            {
                ConsumerName = consumerName,
                StreamName = streamName,
                StartTime = now.AddMinutes(-samples),
                EndTime = now,
                Count = history.Count,
                History = history
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get metrics history for consumer '{consumerName}'", ex);
        }
    }

    /// <summary>
    /// Gets predefined consumer templates
    /// </summary>
    public ConsumerTemplatesResponse GetConsumerTemplates()
    {
        var templates = new List<ConsumerTemplate>
        {
            new ConsumerTemplate
            {
                Name = "real-time-processor",
                Description = "Processes new messages in real-time (ephemeral)",
                UseCase = "Event processing, real-time analytics",
                Template = new CreateConsumerRequest
                {
                    Durable = false, // Ephemeral - temporary consumer
                    DeliverPolicy = "new",
                    AckPolicy = "explicit",
                    MaxDeliver = 3,
                    AckWait = TimeSpan.FromSeconds(30)
                }
            },
            new ConsumerTemplate
            {
                Name = "batch-processor",
                Description = "Processes all messages from the beginning (durable)",
                UseCase = "Batch processing, data migration",
                Template = new CreateConsumerRequest
                {
                    Durable = true, // Durable - persists across restarts
                    DeliverPolicy = "all",
                    AckPolicy = "explicit",
                    MaxDeliver = 5,
                    AckWait = TimeSpan.FromMinutes(5)
                }
            },
            new ConsumerTemplate
            {
                Name = "work-queue",
                Description = "Work queue pattern with explicit acknowledgments (durable)",
                UseCase = "Job processing, task distribution",
                Template = new CreateConsumerRequest
                {
                    Durable = true, // Durable - reliable job processing
                    DeliverPolicy = "all",
                    AckPolicy = "explicit",
                    MaxDeliver = 10,
                    AckWait = TimeSpan.FromMinutes(1),
                    MaxAckPending = 100
                }
            },
            new ConsumerTemplate
            {
                Name = "fire-and-forget",
                Description = "No acknowledgments required (ephemeral)",
                UseCase = "Logging, metrics, non-critical events",
                Template = new CreateConsumerRequest
                {
                    Durable = false, // Ephemeral - non-critical, temporary
                    DeliverPolicy = "new",
                    AckPolicy = "none",
                    MaxDeliver = 1
                }
            },
            new ConsumerTemplate
            {
                Name = "latest-only",
                Description = "Only processes the most recent message (ephemeral)",
                UseCase = "Status updates, latest state",
                Template = new CreateConsumerRequest
                {
                    Durable = false, // Ephemeral - only cares about latest
                    DeliverPolicy = "last",
                    AckPolicy = "explicit",
                    MaxDeliver = 3
                }
            },
            new ConsumerTemplate
            {
                Name = "durable-processor",
                Description = "Durable consumer that survives restarts",
                UseCase = "Critical processing, guaranteed delivery",
                Template = new CreateConsumerRequest
                {
                    Durable = true, // Explicitly durable - critical processing
                    DeliverPolicy = "all",
                    AckPolicy = "explicit",
                    MaxDeliver = -1, // Unlimited retries
                    AckWait = TimeSpan.FromMinutes(10),
                    InactiveThreshold = TimeSpan.FromHours(24),
                    MaxAckPending = 1000
                }
            }
        };

        return new ConsumerTemplatesResponse
        {
            Count = templates.Count,
            Templates = templates
        };
    }

    /// <summary>
    /// Tries to get a string preview of binary data
    /// </summary>
    private static string? TryGetStringPreview(byte[] data, int maxLength)
    {
        try
        {
            var str = Encoding.UTF8.GetString(data);
            return str.Length > maxLength ? str.Substring(0, maxLength) + "..." : str;
        }
        catch
        {
            return $"[binary data, {data.Length} bytes]";
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
                _nats?.DisposeAsync().AsTask().Wait();
            }

            _disposed = true;
        }
    }
}
