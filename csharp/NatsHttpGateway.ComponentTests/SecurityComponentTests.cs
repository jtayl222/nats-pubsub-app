using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;

namespace NatsHttpGateway.ComponentTests;

/// <summary>
/// Component tests for JWT authentication and authorization.
/// These tests verify the security layer works correctly with real HTTP requests
/// against a real NATS JetStream server.
/// </summary>
[TestFixture]
[Category("Component")]
[Category("Security")]
public class SecurityComponentTests : NatsComponentTestBase
{
    /// <summary>
    /// HttpClient without Authorization header for testing unauthenticated requests.
    /// The base class Client has a valid JWT token set by default.
    /// </summary>
    private HttpClient _unauthenticatedClient = null!;

    [OneTimeSetUp]
    public void SecuritySetup()
    {
        // Create a client without Authorization header for testing unauthenticated requests
        // Base class GlobalSetup has already run and created Factory
        _unauthenticatedClient = Factory.CreateClient();
    }

    [OneTimeTearDown]
    public void SecurityTeardown()
    {
        _unauthenticatedClient?.Dispose();
    }

    #region Health Endpoint (AllowAnonymous)

    [Test]
    public async Task HealthEndpoint_WithoutToken_Returns200()
    {
        // Act - Use unauthenticated client
        var response = await _unauthenticatedClient.GetAsync("/health");

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
        var response = await _unauthenticatedClient.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Health endpoint should be accessible even with invalid token ([AllowAnonymous])");
    }

    #endregion

    #region Protected Endpoints - No Token

    [Test]
    public async Task StreamsEndpoint_WithoutToken_Returns401()
    {
        // Act - Use unauthenticated client
        var response = await _unauthenticatedClient.GetAsync("/api/streams");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject requests without token");
    }

    [Test]
    public async Task MessagesEndpoint_WithoutToken_Returns401()
    {
        // Act - Use unauthenticated client
        var response = await _unauthenticatedClient.GetAsync("/api/messages/test.subject");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Messages endpoint should reject requests without token");
    }

    [Test]
    public async Task ConsumersEndpoint_WithoutToken_Returns401()
    {
        // Act - Use unauthenticated client
        var response = await _unauthenticatedClient.GetAsync("/api/consumers/templates");

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
        var response = await _unauthenticatedClient.SendAsync(request);

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
        var response = await _unauthenticatedClient.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject malformed tokens");
    }

    [Test]
    public async Task StreamsEndpoint_WithWrongSigningKey_Returns401()
    {
        // Arrange - Token signed with different key
        var token = GenerateCustomToken(signingKey: "wrong-secret-key-that-does-not-match!!");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _unauthenticatedClient.SendAsync(request);

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
        var token = GenerateCustomToken(expiresIn: TimeSpan.FromMinutes(-10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _unauthenticatedClient.SendAsync(request);

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
        var token = GenerateCustomToken(issuer: "wrong-issuer");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _unauthenticatedClient.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject tokens with wrong issuer");
    }

    [Test]
    public async Task StreamsEndpoint_WithWrongAudience_Returns401()
    {
        // Arrange - Token with wrong audience
        var token = GenerateCustomToken(audience: "wrong-audience");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _unauthenticatedClient.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Streams endpoint should reject tokens with wrong audience");
    }

    #endregion

    #region Protected Endpoints - Valid Token

    [Test]
    public async Task StreamsEndpoint_WithValidToken_Returns200()
    {
        // Act - Use authenticated client from base class (has valid token)
        var response = await Client.GetAsync("/api/streams");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Streams endpoint should accept valid tokens");
    }

    [Test]
    public async Task ConsumersTemplatesEndpoint_WithValidToken_Returns200()
    {
        // Act - Use authenticated client from base class
        var response = await Client.GetAsync("/api/consumers/templates");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Consumer templates endpoint should accept valid tokens");
    }

    #endregion

    #region Authorization Header Formats

    [Test]
    public async Task StreamsEndpoint_WithBearerPrefix_Works()
    {
        // Act - Use authenticated client from base class (uses Bearer prefix)
        var response = await Client.GetAsync("/api/streams");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task StreamsEndpoint_WithoutBearerPrefix_Returns401()
    {
        // Arrange - Token without "Bearer" scheme
        var token = GenerateTestToken();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/streams");
        request.Headers.TryAddWithoutValidation("Authorization", token); // No "Bearer" prefix

        // Act
        var response = await _unauthenticatedClient.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "Token must be prefixed with 'Bearer'");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Generates a JWT token with custom parameters for testing invalid scenarios.
    /// Uses base class JWT constants by default, but allows overriding for negative tests.
    /// </summary>
    private string GenerateCustomToken(
        string? signingKey = null,
        string? issuer = null,
        string? audience = null,
        TimeSpan? expiresIn = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            (signingKey ?? TestJwtKey).PadRight(32, '0')));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer ?? TestJwtIssuer,
            audience: audience ?? TestJwtAudience,
            claims: new[]
            {
                new Claim(ClaimTypes.Name, "security-test-user"),
                new Claim(ClaimTypes.NameIdentifier, "security-test-user-id")
            },
            expires: DateTime.UtcNow.Add(expiresIn ?? TimeSpan.FromHours(1)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #endregion
}
