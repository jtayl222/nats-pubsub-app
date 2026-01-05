using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NatsHttpGateway.Configuration;
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

// Bind configuration options (supports both appsettings.json sections and environment variables)
builder.Services.Configure<JwtOptions>(options =>
{
    // Bind from "Jwt" section first
    builder.Configuration.GetSection(JwtOptions.SectionName).Bind(options);

    // Environment variables override (flat naming convention)
    options.Key = builder.Configuration["JWT_KEY"] ?? options.Key;
    options.Issuer = builder.Configuration["JWT_ISSUER"] ?? options.Issuer;
    options.Audience = builder.Configuration["JWT_AUDIENCE"] ?? options.Audience;
});

builder.Services.Configure<NatsOptions>(options =>
{
    // Bind from "Nats" section first
    builder.Configuration.GetSection(NatsOptions.SectionName).Bind(options);

    // Environment variables override (flat naming convention)
    options.Url = builder.Configuration["NATS_URL"] ?? options.Url;
    options.StreamPrefix = builder.Configuration["STREAM_PREFIX"] ?? options.StreamPrefix;
    options.CaFile = builder.Configuration["NATS_CA_FILE"] ?? options.CaFile;
    options.CertFile = builder.Configuration["NATS_CERT_FILE"] ?? options.CertFile;
    options.KeyFile = builder.Configuration["NATS_KEY_FILE"] ?? options.KeyFile;
});

// Build JwtOptions early to determine if authentication should be enabled
var jwtOptions = new JwtOptions();
builder.Configuration.GetSection(JwtOptions.SectionName).Bind(jwtOptions);
jwtOptions.Key = builder.Configuration["JWT_KEY"] ?? jwtOptions.Key;
jwtOptions.Issuer = builder.Configuration["JWT_ISSUER"] ?? jwtOptions.Issuer;
jwtOptions.Audience = builder.Configuration["JWT_AUDIENCE"] ?? jwtOptions.Audience;

var jwtEnabled = jwtOptions.IsEnabled;

if (jwtEnabled)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrEmpty(jwtOptions.Issuer),
                ValidateAudience = !string.IsNullOrEmpty(jwtOptions.Audience),
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key!)),
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogWarning("JWT authentication failed: {Error}", context.Exception.Message);
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();
}

// Add controllers and API documentation
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NATS HTTP Gateway",
        Version = "v1",
        Description = "HTTP/REST gateway for NATS JetStream messaging"
    });

    // Add JWT authentication to Swagger
    if (jwtEnabled)
    {
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    }

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

// Enable authentication and authorization if JWT is configured
if (jwtEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.Logger.LogInformation("JWT authentication enabled (issuer: {Issuer}, audience: {Audience})",
        jwtOptions.Issuer ?? "any", jwtOptions.Audience ?? "any");
}
else
{
    app.Logger.LogWarning("JWT authentication is DISABLED - set JWT_KEY to enable");
}

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
