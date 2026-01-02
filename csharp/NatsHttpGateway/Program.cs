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

// Add controllers and API documentation
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() {
        Title = "NATS HTTP Gateway",
        Version = "v1",
        Description = "HTTP/REST gateway for NATS JetStream messaging"
    });

    // Include XML comments for Swagger documentation
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Register NATS service as singleton (reuse connection)
builder.Services.AddSingleton<INatsService, NatsService>();

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
// Enable Swagger in all environments for API documentation
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

// Enable WebSocket support
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
};
app.UseWebSockets(webSocketOptions);

// Map controllers
app.MapControllers();

// Root endpoint
app.MapGet("/", () => new
{
    name = "NATS HTTP Gateway",
    version = "1.0.0",
    endpoints = new[]
    {
        "GET /health - Health check",
        "POST /api/messages/{subject} - Publish message",
        "GET /api/messages/{subjectFilter}?limit=10&timeout=5 - Fetch messages (ephemeral)",
        "GET /api/messages/{stream}/consumer/{name}?limit=10&timeout=5 - Fetch messages (durable)",
        "WS  /ws/websocketmessages/{subjectFilter} - Stream messages via WebSocket (ephemeral)",
        "WS  /ws/websocketmessages/{stream}/consumer/{name} - Stream messages via WebSocket (durable)",
        "GET /api/streams - List all JetStream streams",
        "GET /api/streams/{name} - Get stream information",
        "GET /api/streams/{name}/subjects - List subjects in stream",
        "GET /swagger - API documentation"
    }
})
.ExcludeFromDescription();

app.Logger.LogInformation("NATS HTTP Gateway starting on {Urls}", string.Join(", ", builder.WebHost.GetSetting("urls")?.Split(';') ?? new[] { "http://localhost:5000" }));

app.Run();

// Make Program accessible for WebApplicationFactory in tests
public partial class Program { }
