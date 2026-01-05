using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NatsHttpGateway.Models;
using NatsHttpGateway.Services;
using NUnit.Framework;

namespace NatsHttpGateway.ComponentTests;

/// <summary>
/// Component tests for JWT authentication and authorization.
/// These tests verify the security layer works correctly with real HTTP requests.
/// Uses a mock NatsService to isolate JWT authentication testing from NATS connectivity.
/// </summary>
[TestFixture]
[Category("Component")]
[Category("Security")]
public class SecurityComponentTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private const string TestJwtKey = "test-secret-key-for-jwt-validation-minimum-32-characters";
    private const string TestIssuer = "test-issuer";
    private const string TestAudience = "nats-gateway";

    [OneTimeSetUp]
    public void GlobalSetup()
    {
        // Set environment variables BEFORE creating WebApplicationFactory
        // These must be set before the app starts since Program.cs reads them at startup
        Environment.SetEnvironmentVariable("JWT_KEY", TestJwtKey);
        Environment.SetEnvironmentVariable("JWT_ISSUER", TestIssuer);
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", TestAudience);
        Environment.SetEnvironmentVariable("NATS_URL", "nats://localhost:4222");

        // Create mock NatsService to isolate security testing from NATS connectivity
        var mockNatsService = new Mock<INatsService>();
        mockNatsService.Setup(s => s.IsConnected).Returns(true);
        mockNatsService.Setup(s => s.IsJetStreamAvailable).Returns(true);
        mockNatsService.Setup(s => s.NatsUrl).Returns("nats://localhost:4222");
        mockNatsService.Setup(s => s.ListStreamsAsync()).ReturnsAsync(new List<StreamSummary>());
        mockNatsService.Setup(s => s.GetConsumerTemplates()).Returns(new ConsumerTemplatesResponse
        {
            Count = 0,
            Templates = new List<ConsumerTemplate>()
        });

        // Configure the web application with mock NatsService
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Use ConfigureTestServices which runs AFTER Program.cs ConfigureServices
                // This ensures our mock replaces the real NatsService
                builder.ConfigureTestServices(services =>
                {
                    // Remove all NatsService registrations
                    var descriptors = services.Where(d => d.ServiceType == typeof(INatsService)).ToList();
                    foreach (var descriptor in descriptors)
                    {
                        services.Remove(descriptor);
                    }

                    // Add mock as singleton - will be used instead of real NatsService
                    services.AddSingleton<INatsService>(mockNatsService.Object);
                });
            });

        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        _client?.Dispose();
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }

        // Clean up environment variables
        Environment.SetEnvironmentVariable("JWT_KEY", null);
        Environment.SetEnvironmentVariable("JWT_ISSUER", null);
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", null);
    }

    #region Health Endpoint (AllowAnonymous)

    [Test]
    public async Task HealthEndpoint_WithoutToken_Returns200()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Health endpoint should be accessible without authentication");
    }

    [Test]
    public async Task HealthEndpoint_WithInvalidToken_Returns200()
    {
        // Arrange - Add invalid token
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.token.here");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Health endpoint should be accessible even with invalid token ([AllowAnonymous])");
    }

    #endregion

    #region Protected Endpoints - No Token

    [Test]
    public async Task StreamsEndpoint_WithoutToken_Returns401()
    {
        // Act
        var response = await _client.GetAsync("/api/streams");

        // Debug: Log response if not expected
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            var content = await response.Content.ReadAsStringAsync();
            TestContext.WriteLine($"Unexpected response ({response.StatusCode}): {content}");
        }

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject requests without token");
    }

    [Test]
    public async Task MessagesEndpoint_WithoutToken_Returns401()
    {
        // Act
        var response = await _client.GetAsync("/api/messages/test.subject");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Messages endpoint should reject requests without token");
    }

    [Test]
    public async Task ConsumersEndpoint_WithoutToken_Returns401()
    {
        // Act
        var response = await _client.GetAsync("/api/consumers/templates");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Consumers endpoint should reject requests without token");
    }

    #endregion

    #region Protected Endpoints - Invalid Token

    [Test]
    public async Task StreamsEndpoint_WithInvalidToken_Returns401()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.token.here");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject invalid tokens");
    }

    [Test]
    public async Task StreamsEndpoint_WithMalformedToken_Returns401()
    {
        // Arrange - Token without proper JWT structure
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject malformed tokens");
    }

    [Test]
    public async Task StreamsEndpoint_WithWrongSigningKey_Returns401()
    {
        // Arrange - Token signed with different key
        var token = GenerateJwtToken("wrong-secret-key-that-does-not-match");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject tokens signed with wrong key");
    }

    #endregion

    #region Protected Endpoints - Expired Token

    [Test]
    public async Task StreamsEndpoint_WithExpiredToken_Returns401()
    {
        // Arrange - Generate clearly expired token (beyond 5-minute clock skew tolerance)
        var token = GenerateJwtToken(TestJwtKey, expiresIn: TimeSpan.FromMinutes(-10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject expired tokens");
    }

    #endregion

    #region Protected Endpoints - Wrong Issuer/Audience

    [Test]
    public async Task StreamsEndpoint_WithWrongIssuer_Returns401()
    {
        // Arrange - Token with wrong issuer
        var token = GenerateJwtToken(TestJwtKey, issuer: "wrong-issuer");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject tokens with wrong issuer");
    }

    [Test]
    public async Task StreamsEndpoint_WithWrongAudience_Returns401()
    {
        // Arrange - Token with wrong audience
        var token = GenerateJwtToken(TestJwtKey, audience: "wrong-audience");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject tokens with wrong audience");
    }

    #endregion

    #region Protected Endpoints - Valid Token

    [Test]
    public async Task StreamsEndpoint_WithValidToken_Returns200()
    {
        // Arrange - Generate valid token
        var token = GenerateJwtToken(TestJwtKey);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Streams endpoint should accept valid tokens");
    }

    [Test]
    public async Task ConsumersTemplatesEndpoint_WithValidToken_Returns200()
    {
        // Arrange
        var token = GenerateJwtToken(TestJwtKey);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/consumers/templates");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Consumer templates endpoint should accept valid tokens");
    }

    #endregion

    #region Authorization Header Formats

    [Test]
    public async Task StreamsEndpoint_WithBearerPrefix_Works()
    {
        // Arrange
        var token = GenerateJwtToken(TestJwtKey);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task StreamsEndpoint_WithoutBearerPrefix_Returns401()
    {
        // Arrange - Token without "Bearer" scheme
        var token = GenerateJwtToken(TestJwtKey);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.TryAddWithoutValidation("Authorization", token); // No "Bearer" prefix

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Token must be prefixed with 'Bearer'");
    }

    #endregion

    #region Helper Methods

    private static string GenerateJwtToken(
        string signingKey,
        string? issuer = null,
        string? audience = null,
        TimeSpan? expiresIn = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey.PadRight(32, '0')));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer ?? TestIssuer,
            audience: audience ?? TestAudience,
            claims: new[]
            {
                new Claim(ClaimTypes.Name, "test-user"),
                new Claim(ClaimTypes.NameIdentifier, "test-user-id")
            },
            expires: DateTime.UtcNow.Add(expiresIn ?? TimeSpan.FromHours(1)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #endregion
}
