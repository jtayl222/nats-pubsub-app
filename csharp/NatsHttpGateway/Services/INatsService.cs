using NatsHttpGateway.Models;

namespace NatsHttpGateway.Services;

public interface INatsService
{
    bool IsConnected { get; }
    bool IsJetStreamAvailable { get; }
    string NatsUrl { get; }

    Task<PublishResponse> PublishAsync(string subject, PublishRequest request);
    Task<FetchMessagesResponse> FetchMessagesAsync(string subject, int limit = 10);
    Task<List<StreamSummary>> ListStreamsAsync();
    Task<StreamSummary> GetStreamInfoAsync(string name);
    Task<StreamSubjectsResponse> GetStreamSubjectsAsync(string name);
}
