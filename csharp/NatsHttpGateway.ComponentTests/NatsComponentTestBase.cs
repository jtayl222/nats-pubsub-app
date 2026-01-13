using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NUnit.Framework;
using Testcontainers.Nats;

namespace NatsHttpGateway.ComponentTests;

/// <summary>
/// Base class for component tests that run against NATS JetStream.
///
/// By default, spins up a NATS container using Testcontainers for isolated, reproducible tests.
/// Set USE_EXTERNAL_NATS=true to use an external NATS server instead (for CI or local development).
/// </summary>
[TestFixture]
[Category("Component")]
public abstract class NatsComponentTestBase
{
    private NatsContainer? _natsContainer;

    protected WebApplicationFactory<Program> Factory = null!;
    protected HttpClient Client = null!;
    protected NatsConnection NatsConnection = null!;
    protected INatsJSContext JetStream = null!;
    protected string TestStreamName = null!;
    protected string NatsUrl = null!;

    private static bool UseExternalNats =>
        Environment.GetEnvironmentVariable("USE_EXTERNAL_NATS")?.ToLower() == "true";

    private static string ExternalNatsUrl =>
        Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";

    private static string? JwtToken =>
        Environment.GetEnvironmentVariable("JWT_TOKEN");

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        if (UseExternalNats)
        {
            NatsUrl = ExternalNatsUrl;
            TestContext.WriteLine($"Using external NATS server: {NatsUrl}");
        }
        else
        {
            // Start NATS container with JetStream enabled
            _natsContainer = new NatsBuilder()
                .WithImage("nats:2.10-alpine")
                .WithCommand("--jetstream")
                .Build();

            await _natsContainer.StartAsync();
            NatsUrl = _natsContainer.GetConnectionString();
            TestContext.WriteLine($"Started NATS container: {NatsUrl}");
        }

        // Set environment variable for the web application
        Environment.SetEnvironmentVariable("NATS_URL", NatsUrl);

        if (!string.IsNullOrEmpty(JwtToken))
        {
            Environment.SetEnvironmentVariable("JWT_TOKEN", JwtToken);
        }

        // Configure the web application
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    var configValues = new Dictionary<string, string?>
                    {
                        ["NATS_URL"] = NatsUrl
                    };

                    if (!string.IsNullOrEmpty(JwtToken))
                    {
                        configValues["JWT_TOKEN"] = JwtToken;
                    }

                    config.AddInMemoryCollection(configValues);
                });
            });

        Client = Factory.CreateClient();

        // Create direct NATS connection for test setup/verification
        var opts = CreateNatsOpts();
        NatsConnection = new NatsConnection(opts);
        await NatsConnection.ConnectAsync();
        JetStream = new NatsJSContext(NatsConnection);

        // Wait for NATS to be fully ready
        await WaitForNatsReadyAsync();
    }

    private NatsOpts CreateNatsOpts()
    {
        var opts = new NatsOpts { Url = NatsUrl };

        if (!string.IsNullOrEmpty(JwtToken) && UseExternalNats)
        {
            opts = opts with
            {
                AuthOpts = new NatsAuthOpts { Jwt = JwtToken }
            };
            TestContext.WriteLine("Using JWT authentication for NATS connection");
        }

        return opts;
    }

    /// <summary>
    /// Waits for NATS JetStream to be ready by attempting to list streams.
    /// </summary>
    private async Task WaitForNatsReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // Try to list streams - this confirms JetStream is ready
                await foreach (var _ in JetStream.ListStreamsAsync())
                {
                    break;
                }
                TestContext.WriteLine("NATS JetStream is ready");
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(100);
            }
        }

        throw new TimeoutException(
            $"NATS JetStream not ready after 30 seconds. Last error: {lastException?.Message}");
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

        if (_natsContainer != null)
        {
            await _natsContainer.DisposeAsync();
            TestContext.WriteLine("NATS container stopped");
        }
    }

    /// <summary>
    /// Helper for eventual consistency - retries a condition until it passes or times out.
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
