using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NUnit.Framework;

namespace NatsHttpGateway.ComponentTests;

/// <summary>
/// Base class for component tests that run against a real NATS JetStream server.
/// Provides WebApplicationFactory for API testing and direct NATS connection for verification.
///
/// Supports JWT authentication via the JWT_TOKEN environment variable.
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
    private static string? JwtToken => Environment.GetEnvironmentVariable("JWT_TOKEN");

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Set environment variable for the test host (NatsService reads from configuration["NATS_URL"])
        Environment.SetEnvironmentVariable("NATS_URL", NatsUrl);

        // Configure JWT_TOKEN for the web application if provided
        if (!string.IsNullOrEmpty(JwtToken))
        {
            Environment.SetEnvironmentVariable("JWT_TOKEN", JwtToken);
        }

        // Configure the web application to use the test NATS server
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    var configValues = new Dictionary<string, string?>
                    {
                        ["NATS_URL"] = NatsUrl
                    };

                    // Add JWT_TOKEN to configuration if provided
                    if (!string.IsNullOrEmpty(JwtToken))
                    {
                        configValues["JWT_TOKEN"] = JwtToken;
                    }

                    config.AddInMemoryCollection(configValues);
                });
            });

        Client = Factory.CreateClient();

        // Direct NATS connection for test setup/verification
        var opts = CreateNatsOpts();
        NatsConnection = new NatsConnection(opts);
        await NatsConnection.ConnectAsync();
        JetStream = new NatsJSContext(NatsConnection);
    }

    /// <summary>
    /// Creates NatsOpts with JWT authentication if JWT_TOKEN is provided.
    /// </summary>
    private static NatsOpts CreateNatsOpts()
    {
        var opts = new NatsOpts { Url = NatsUrl };

        if (!string.IsNullOrEmpty(JwtToken))
        {
            // Configure JWT authentication
            opts = opts with
            {
                AuthOpts = new NatsAuthOpts
                {
                    Jwt = JwtToken
                }
            };
            TestContext.WriteLine($"Using JWT authentication for NATS connection");
        }

        return opts;
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
