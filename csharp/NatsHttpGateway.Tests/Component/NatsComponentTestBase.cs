using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NUnit.Framework;

namespace NatsHttpGateway.Tests.Component;

/// <summary>
/// Base class for component tests that run against a real NATS JetStream server.
/// Provides WebApplicationFactory for API testing and direct NATS connection for verification.
/// </summary>
[TestFixture]
[Category("Component")]
public abstract class NatsComponentTestBase
{
    protected WebApplicationFactory<Program> Factory = null!;
    protected HttpClient Client = null!;
    protected NatsConnection NatsConnection = null!;
    protected INatsJSContext JetStream = null!;
    protected string TestStreamName = null!;

    private static string NatsUrl => Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Set environment variable for the test host (NatsService reads from configuration["NATS_URL"])
        Environment.SetEnvironmentVariable("NATS_URL", NatsUrl);

        // Configure the web application to use the test NATS server
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["NATS_URL"] = NatsUrl
                    });
                });
            });

        Client = Factory.CreateClient();

        // Direct NATS connection for test setup/verification
        var opts = new NatsOpts { Url = NatsUrl };
        NatsConnection = new NatsConnection(opts);
        await NatsConnection.ConnectAsync();
        JetStream = new NatsJSContext(NatsConnection);
    }

    [SetUp]
    public void TestSetup()
    {
        // Unique stream per test ensures isolation
        TestStreamName = $"TEST_{Guid.NewGuid():N}";
    }

    [TearDown]
    public async Task TestTeardown()
    {
        // Clean up test stream if it was created
        try
        {
            await JetStream.DeleteStreamAsync(TestStreamName);
        }
        catch (NATS.Client.JetStream.NatsJSApiException)
        {
            // Stream may not exist - that's OK
        }
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        Client?.Dispose();
        if (Factory != null)
        {
            await Factory.DisposeAsync();
        }
        if (NatsConnection != null)
        {
            await NatsConnection.DisposeAsync();
        }
    }

    /// <summary>
    /// Helper for eventual consistency - retries a condition until it passes or times out.
    /// Use this when assertions may need to wait for NATS to propagate state.
    /// </summary>
    protected async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition not met within timeout period");
    }

    /// <summary>
    /// Gets the WebSocket URI for the test server.
    /// </summary>
    protected Uri GetWebSocketUri(string path)
    {
        var baseUri = Factory.Server.BaseAddress;
        var wsScheme = baseUri.Scheme == "https" ? "wss" : "ws";
        return new Uri($"{wsScheme}://{baseUri.Host}:{baseUri.Port}{path}");
    }
}
