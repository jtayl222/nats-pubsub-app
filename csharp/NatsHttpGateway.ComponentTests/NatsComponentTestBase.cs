using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NatsHttpGateway.Configuration;
using NUnit.Framework;

namespace NatsHttpGateway.ComponentTests;

/// <summary>
/// Base class for component tests that run against a real NATS JetStream server.
/// Provides WebApplicationFactory for API testing and direct NATS connection for verification.
///
/// Configuration is loaded from appsettings.json with environment variable overrides using IOptions pattern.
/// JWT is always enabled for component tests to allow authenticated API requests.
/// Supports dual security layers:
/// - REST API: JWT authentication (auto-configured for tests)
/// - NATS: mTLS via Nats section
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

    // Test JWT configuration - always enabled for component tests
    protected const string TestJwtKey = "component-test-jwt-secret-key-min-32-chars!!";
    protected const string TestJwtIssuer = "component-test-issuer";
    protected const string TestJwtAudience = "component-test-audience";

    // Configuration loaded from appsettings.json + environment variables
    private static IConfiguration? _configuration;
    protected static IConfiguration Configuration => _configuration ??= BuildConfiguration();

    // Strongly-typed options
    private static NatsOptions? _natsOptions;
    protected static NatsOptions NatsConfig => _natsOptions ??= BuildNatsOptions();

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    private static NatsOptions BuildNatsOptions()
    {
        var options = new NatsOptions();
        Configuration.GetSection(NatsOptions.SectionName).Bind(options);

        // Environment variables override
        options.Url = Configuration["NATS_URL"] ?? options.Url;
        options.StreamPrefix = Configuration["STREAM_PREFIX"] ?? options.StreamPrefix;
        options.CaFile = Configuration["NATS_CA_FILE"] ?? options.CaFile;
        options.CertFile = Configuration["NATS_CERT_FILE"] ?? options.CertFile;
        options.KeyFile = Configuration["NATS_KEY_FILE"] ?? options.KeyFile;

        return options;
    }

    /// <summary>
    /// Generates a valid JWT token for component test API requests.
    /// </summary>
    protected static string GenerateTestToken(TimeSpan? expiry = null)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "component-test-user"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("name", "Component Test User")
        };

        var token = new JwtSecurityToken(
            issuer: TestJwtIssuer,
            audience: TestJwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.Add(expiry ?? TimeSpan.FromHours(1)),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        TestContext.WriteLine($"Loading configuration from: {Path.Combine(AppContext.BaseDirectory, "appsettings.json")}");

        // CRITICAL: Set environment variables BEFORE creating WebApplicationFactory
        // Program.cs reads these at startup to configure JWT authentication
        Environment.SetEnvironmentVariable("JWT_KEY", TestJwtKey);
        Environment.SetEnvironmentVariable("JWT_ISSUER", TestJwtIssuer);
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", TestJwtAudience);
        Environment.SetEnvironmentVariable("NATS_URL", NatsConfig.Url);

        if (NatsConfig.IsTlsEnabled)
            Environment.SetEnvironmentVariable("NATS_CA_FILE", NatsConfig.CaFile);
        if (NatsConfig.IsMtlsEnabled)
        {
            Environment.SetEnvironmentVariable("NATS_CERT_FILE", NatsConfig.CertFile);
            Environment.SetEnvironmentVariable("NATS_KEY_FILE", NatsConfig.KeyFile);
        }

        // Configure the web application with test settings
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Override NATS options with test configuration
                    services.Configure<NatsOptions>(opts =>
                    {
                        opts.Url = NatsConfig.Url;
                        opts.StreamPrefix = NatsConfig.StreamPrefix;
                        opts.CaFile = NatsConfig.CaFile;
                        opts.CertFile = NatsConfig.CertFile;
                        opts.KeyFile = NatsConfig.KeyFile;
                    });

                    // Override JWT options
                    services.Configure<JwtOptions>(opts =>
                    {
                        opts.Key = TestJwtKey;
                        opts.Issuer = TestJwtIssuer;
                        opts.Audience = TestJwtAudience;
                    });
                });
            });

        Client = Factory.CreateClient();

        // Generate and set test JWT token for authenticated API requests
        var testToken = GenerateTestToken();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", testToken);
        TestContext.WriteLine("Using auto-generated JWT Bearer token for API requests");

        // Direct NATS connection for test setup/verification (with mTLS support)
        var opts = CreateNatsOpts();
        NatsConnection = new NatsConnection(opts);
        await NatsConnection.ConnectAsync();
        JetStream = new NatsJSContext(NatsConnection);

        TestContext.WriteLine($"Connected to NATS at {NatsConfig.Url}");
    }

    /// <summary>
    /// Creates NatsOpts with mTLS if certificate files are provided.
    /// </summary>
    private static NatsOpts CreateNatsOpts()
    {
        var opts = new NatsOpts { Url = NatsConfig.Url };

        // Configure mTLS if certificate files are provided
        if (NatsConfig.IsMtlsEnabled)
        {
            opts = opts with { TlsOpts = CreateTlsOpts() };
            TestContext.WriteLine($"Using mTLS for NATS connection (cert: {NatsConfig.CertFile})");
        }
        else if (NatsConfig.IsTlsEnabled)
        {
            opts = opts with { TlsOpts = CreateTlsOpts() };
            TestContext.WriteLine($"Using TLS with CA verification for NATS connection");
        }

        return opts;
    }

    /// <summary>
    /// Creates TLS options for NATS connection with optional mTLS.
    /// </summary>
    private static NatsTlsOpts CreateTlsOpts()
    {
        return new NatsTlsOpts
        {
            Mode = TlsMode.Require,
            CaFile = NatsConfig.CaFile,
            CertFile = NatsConfig.CertFile,
            KeyFile = NatsConfig.KeyFile
        };
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
