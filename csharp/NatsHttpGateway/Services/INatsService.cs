using NatsHttpGateway.Models;

namespace NatsHttpGateway.Services;

public interface INatsService
{
    bool IsConnected { get; }
    bool IsJetStreamAvailable { get; }
    string NatsUrl { get; }

    Task<PublishResponse> PublishAsync(string subject, PublishRequest request);
    Task<FetchMessagesResponse> FetchMessagesAsync(string subjectFilter, int limit = 10, int timeoutSeconds = 5);
    Task<FetchMessagesResponse> FetchMessagesFromConsumerAsync(string streamName, string consumerName, int limit = 10, int timeoutSeconds = 5);
    IAsyncEnumerable<MessageResponse> StreamMessagesAsync(string subjectFilter, CancellationToken cancellationToken);
    IAsyncEnumerable<MessageResponse> StreamMessagesFromConsumerAsync(string streamName, string consumerName, CancellationToken cancellationToken);
    Task<List<StreamSummary>> ListStreamsAsync();
    Task<StreamSummary> GetStreamInfoAsync(string name);
    Task<StreamSubjectsResponse> GetStreamSubjectsAsync(string name);

    // Consumer management
    Task<ConsumerDetails> CreateConsumerAsync(string streamName, CreateConsumerRequest request);
    Task<ConsumerListResult> ListConsumersAsync(string streamName);
    Task<ConsumerDetails> GetConsumerInfoAsync(string streamName, string consumerName);
    Task<ConsumerDeleteResult> DeleteConsumerAsync(string streamName, string consumerName);
    Task<ConsumerHealthResponse> GetConsumerHealthAsync(string streamName, string consumerName);

    // Advanced consumer operations
    Task<ConsumerPeekMessagesResponse> PeekConsumerMessagesAsync(string streamName, string consumerName, int limit = 10);
    Task<ConsumerResetResponse> ResetConsumerAsync(string streamName, string consumerName, ConsumerResetRequest request);
    Task<ConsumerMetricsHistoryResponse> GetConsumerMetricsHistoryAsync(string streamName, string consumerName, int samples = 10);
    ConsumerTemplatesResponse GetConsumerTemplates();
}
