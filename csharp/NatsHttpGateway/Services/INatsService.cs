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
}
