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
/// Configuration is loaded from NatsHttpGateway/appsettings.json with environment variable overrides.
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
    protected string NatsUrl = null!;

    private static IConfiguration? _configuration;

    /// <summary>
    /// Loads configuration from appsettings.json with environment variable overrides.
    /// Environment variables take precedence over appsettings values.
    /// </summary>
    private static IConfiguration Configuration
    {
        get
        {
            if (_configuration == null)
            {
                // Find the solution root by looking for the NatsHttpGateway directory
                // Start from the assembly location and walk up until we find it
                var assemblyDir = Path.GetDirectoryName(typeof(NatsComponentTestBase).Assembly.Location)!;
                var searchDir = assemblyDir;
                string? appSettingsPath = null;

                // Walk up the directory tree looking for NatsHttpGateway/appsettings.json
                for (int i = 0; i < 10 && searchDir != null; i++)
                {
                    var candidate = Path.Combine(searchDir, "NatsHttpGateway", "appsettings.json");
                    if (File.Exists(candidate))
                    {
                        appSettingsPath = candidate;
                        break;
                    }
                    searchDir = Path.GetDirectoryName(searchDir);
                }

                var builder = new ConfigurationBuilder();

                if (appSettingsPath != null)
                {
                    var appSettingsDir = Path.GetDirectoryName(appSettingsPath)!;
                    builder
                        .SetBasePath(appSettingsDir)
                        .AddJsonFile("appsettings.json", optional: true)
                        .AddJsonFile("appsettings.Development.json", optional: true);
                }

                _configuration = builder
                    .AddEnvironmentVariables()
                    .Build();
            }
            return _configuration;
        }
    }

    private static string ConfiguredNatsUrl => Configuration["NATS_URL"] ?? "nats://localhost:4222";
    private static string? JwtToken => Configuration["GATEWAY_JWT_TOKEN"];

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        NatsUrl = ConfiguredNatsUrl;
        TestContext.WriteLine($"Using NATS server: {NatsUrl}");

        // Set environment variable for the test host (NatsService reads from configuration["NATS_URL"])
        Environment.SetEnvironmentVariable("NATS_URL", NatsUrl);

        // Configure GATEWAY_JWT_TOKEN for the web application if provided
        if (!string.IsNullOrEmpty(JwtToken))
        {
            Environment.SetEnvironmentVariable("GATEWAY_JWT_TOKEN", JwtToken);
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

                    // Add GATEWAY_JWT_TOKEN to configuration if provided
                    if (!string.IsNullOrEmpty(JwtToken))
                    {
                        configValues["GATEWAY_JWT_TOKEN"] = JwtToken;
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

        // Wait for NATS to be fully ready
        await WaitForNatsReadyAsync();
    }

    /// <summary>
    /// Creates NatsOpts with JWT authentication if GATEWAY_JWT_TOKEN is provided.
    /// </summary>
    private NatsOpts CreateNatsOpts()
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
