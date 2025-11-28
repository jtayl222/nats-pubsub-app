# Quick Start Guide - NATS HTTP Gateway

Get up and running with the NATS HTTP Gateway in 5 minutes.

## Option 1: Local Development (Fastest)

### Prerequisites
- .NET 8 SDK
- NATS server running locally

```bash
# Terminal 1: Start NATS (if not already running)
docker run -p 4222:4222 -p 8222:8222 nats:latest -js

# Terminal 2: Run the gateway
cd csharp/NatsHttpGateway
dotnet run

# Gateway starts on http://localhost:5000
```

## Option 2: Docker Compose (Recommended)

```bash
cd csharp

# Start NATS + Gateway
docker-compose up -d nats nats-http-gateway

# Check logs
docker-compose logs -f nats-http-gateway

# Gateway available at http://localhost:8080
```

## First API Call

### 1. Health Check

```bash
curl http://localhost:8080/health
```

Expected response:
```json
{
  "status": "healthy",
  "nats_connected": true,
  "nats_url": "nats://localhost:4222",
  "jetstream_available": true,
  "timestamp": "2025-11-24T22:00:00.000Z"
}
```

### 2. Publish Your First Message

```bash
curl -X POST http://localhost:8080/api/messages/events.test \
  -H "Content-Type: application/json" \
  -d '{
    "data": {
      "message": "Hello from HTTP Gateway!",
      "timestamp": "2025-11-24T22:00:00Z"
    }
  }'
```

Expected response:
```json
{
  "published": true,
  "subject": "events.test",
  "stream": "EVENTS",
  "sequence": 1,
  "timestamp": "2025-11-24T22:00:00.000Z"
}
```

### 3. Fetch Messages

```bash
curl "http://localhost:8080/api/messages/events.test?limit=10"
```

Expected response:
```json
{
  "subject": "events.test",
  "count": 1,
  "stream": "EVENTS",
  "messages": [
    {
      "subject": "events.test",
      "sequence": 1,
      "timestamp": "2025-11-24T22:00:00.000Z",
      "data": {
        "message_id": "550e8400-e29b-41d4-a716-446655440000",
        "timestamp": "2025-11-24T22:00:00.000Z",
        "source": "http-gateway",
        "data": {
          "message": "Hello from HTTP Gateway!",
          "timestamp": "2025-11-24T22:00:00Z"
        }
      },
      "size_bytes": 156
    }
  ]
}
```

## Interactive API Documentation

Open Swagger UI in your browser:
```
http://localhost:8080/swagger
```

Features:
- ✅ Try-it-out functionality
- ✅ Request/response examples
- ✅ Schema definitions
- ✅ Export as OpenAPI spec

## Common Use Cases

### Webhook Integration

Receive webhooks and forward to NATS:

```bash
# Your webhook receiver script
curl -X POST http://localhost:8080/api/messages/webhooks.github \
  -H "Content-Type: application/json" \
  -d "$GITHUB_WEBHOOK_PAYLOAD"
```

### Mobile App Backend

```bash
# User action from mobile app
curl -X POST http://localhost:8080/api/messages/mobile.user.action \
  -H "Content-Type: application/json" \
  -d '{
    "data": {
      "user_id": 12345,
      "action": "purchase",
      "product_id": "PROD-789",
      "amount": 29.99
    }
  }'
```

### System Monitoring

```bash
# Fetch recent errors
curl "http://localhost:8080/api/messages/system.errors?limit=50"
```

## Testing with Different Tools

### cURL (Command Line)

```bash
# POST
curl -X POST http://localhost:8080/api/messages/test.subject \
  -H "Content-Type: application/json" \
  -d '{"data": {"key": "value"}}'

# GET
curl "http://localhost:8080/api/messages/test.subject?limit=10"
```

### HTTPie (Pretty Output)

```bash
# POST
http POST http://localhost:8080/api/messages/test.subject \
  data:='{"key": "value"}'

# GET
http GET "http://localhost:8080/api/messages/test.subject?limit=10"
```

### JavaScript/Fetch

```javascript
// Publish
const response = await fetch('http://localhost:8080/api/messages/events.test', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    data: { event_type: 'user.login', user_id: 123 }
  })
});
const result = await response.json();

// Fetch
const messages = await fetch('http://localhost:8080/api/messages/events.test?limit=10')
  .then(res => res.json());
```

### Python

```python
import requests

# Publish
response = requests.post(
    'http://localhost:8080/api/messages/events.test',
    json={'data': {'event_type': 'user.login', 'user_id': 123}}
)
print(response.json())

# Fetch
response = requests.get(
    'http://localhost:8080/api/messages/events.test',
    params={'limit': 10}
)
messages = response.json()
```

## Environment Configuration

### Local Development

Edit `appsettings.Development.json`:

```json
{
  "NATS_URL": "nats://localhost:4222",
  "STREAM_PREFIX": "EVENTS"
}
```

### Docker

Set environment variables in `docker-compose.yml` or via command line:

```bash
docker run -p 8080:8080 \
  -e NATS_URL=nats://nats-server:4222 \
  -e STREAM_PREFIX=CUSTOM \
  nats-http-gateway
```

## Troubleshooting

### Gateway won't start

**Error:** Connection refused

```bash
# Check NATS is running
curl http://localhost:8222/healthz

# If not, start NATS:
docker run -d -p 4222:4222 -p 8222:8222 nats:latest -js
```

### 404 Not Found on fetch

**Error:** Stream not found

**Solution:** Publish at least one message to create the stream:

```bash
curl -X POST http://localhost:8080/api/messages/events.test \
  -H "Content-Type: application/json" \
  -d '{"data": {"init": true}}'
```

### Port 8080 already in use

Change the port in docker-compose.yml:

```yaml
ports:
  - "8081:8080"  # External:Internal
```

Or for local dev, set in appsettings:

```json
{
  "ASPNETCORE_URLS": "http://+:5001"
}
```

## Next Steps

1. ✅ Try the [examples.http](examples.http) file with REST Client extension
2. ✅ Explore [Swagger UI](http://localhost:8080/swagger) documentation
3. ✅ Read the full [README.md](README.md) for advanced features
4. ✅ Integrate with your application using your preferred HTTP client

## Production Checklist

Before deploying to production:

- [ ] Add authentication (JWT, API keys, etc.)
- [ ] Enable HTTPS/TLS
- [ ] Configure rate limiting
- [ ] Set up monitoring and logging
- [ ] Configure CORS properly
- [ ] Set resource limits
- [ ] Enable health checks in load balancer
- [ ] Review stream retention policies

For production best practices, see the main [README.md](README.md).
