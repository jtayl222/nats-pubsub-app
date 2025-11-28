using NatsHttpGateway.Models;
using NatsHttpGateway.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Indented = false
    };
});

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() {
        Title = "NATS HTTP Gateway",
        Version = "v1",
        Description = "HTTP/REST gateway for NATS JetStream messaging"
    });
});

// Register NATS service as singleton (reuse connection)
builder.Services.AddSingleton<NatsService>();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Health check endpoint
app.MapGet("/health", (NatsService nats) =>
{
    return Results.Ok(new HealthResponse
    {
        Status = nats.IsConnected ? "healthy" : "unhealthy",
        NatsConnected = nats.IsConnected,
        NatsUrl = nats.NatsUrl,
        JetStreamAvailable = nats.IsJetStreamAvailable,
        Timestamp = DateTime.UtcNow
    });
})
.WithName("HealthCheck")
.WithTags("Health")
.WithOpenApi();

// POST /api/messages/{subject} - Publish message to subject
app.MapPost("/api/messages/{subject}", async (
    string subject,
    PublishRequest request,
    NatsService nats,
    ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("Publishing message to subject: {Subject}", subject);
        var response = await nats.PublishAsync(subject, request);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to publish message to {Subject}", subject);
        return Results.Problem(
            title: "Publish failed",
            detail: ex.Message,
            statusCode: 500
        );
    }
})
.WithName("PublishMessage")
.WithTags("Messages")
.WithOpenApi(operation =>
{
    operation.Summary = "Publish a message to a NATS subject";
    operation.Description = "Publishes a message to the specified NATS subject via JetStream. The stream will be auto-created if it doesn't exist.";
    return operation;
});

// GET /api/messages/{subject} - Fetch last N messages
app.MapGet("/api/messages/{subject}", async (
    string subject,
    int limit,
    NatsService nats,
    ILogger<Program> logger) =>
{
    try
    {
        if (limit < 1 || limit > 100)
        {
            return Results.BadRequest(new { error = "Limit must be between 1 and 100" });
        }

        logger.LogInformation("Fetching {Limit} messages from subject: {Subject}", limit, subject);
        var response = await nats.FetchMessagesAsync(subject, limit);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to fetch messages from {Subject}", subject);
        return Results.Problem(
            title: "Fetch failed",
            detail: ex.Message,
            statusCode: 500
        );
    }
})
.WithName("FetchMessages")
.WithTags("Messages")
.WithOpenApi(operation =>
{
    operation.Summary = "Fetch messages from a NATS subject";
    operation.Description = "Retrieves the last N messages from the specified NATS subject. This is a stateless operation using ephemeral consumers.";
    return operation;
});

// Root endpoint
app.MapGet("/", () => new
{
    name = "NATS HTTP Gateway",
    version = "1.0.0",
    endpoints = new[]
    {
        "GET /health - Health check",
        "POST /api/messages/{subject} - Publish message",
        "GET /api/messages/{subject}?limit=10 - Fetch messages",
        "GET /swagger - API documentation"
    }
})
.WithName("Root")
.WithOpenApi()
.ExcludeFromDescription();

app.Logger.LogInformation("NATS HTTP Gateway starting on {Urls}", string.Join(", ", builder.WebHost.GetSetting("urls")?.Split(';') ?? new[] { "http://localhost:5000" }));

app.Run();
