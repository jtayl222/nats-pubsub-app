# NATS Message Logging to Loki

How to capture NATS messages from any application and send them to Loki for analysis.

## Understanding the Data Flow

### Current Architecture

```
┌──────────────┐                  ┌──────────────┐
│ Any App      │─────publish────► │     NATS     │
│ Publishing   │   (any topic)    │    Server    │
└──────────────┘                  └──────────────┘
                                         │
                                         │ deliver
                                         ▼
                                  ┌──────────────┐
                                  │  Message     │
                                  │  Logger      │
                                  │  (subscribes │
                                  │   to all)    │
                                  └──────┬───────┘
                                         │
                                         │ logs messages
                                         ▼
                                  ┌──────────────┐
                                  │   Promtail   │
                                  │  (collects)  │
                                  └──────┬───────┘
                                         │
                                         ▼
                                  ┌──────────────┐
                                  │     Loki     │
                                  │   (stores)   │
                                  └──────────────┘
                                         │
                                         ▼
                                  ┌──────────────┐
                                  │   Grafana    │
                                  │   (query)    │
                                  └──────────────┘
```

## Two Types of Data

### 1. Application Logs (What you have now)

Your Publisher and Subscriber apps log their **own activity**:

```json
{
  "timestamp": "2025-10-09T01:48:15.988Z",
  "level": "INFO",
  "logger": "nats-publisher",
  "message": "Message published",
  "subject": "events.test",
  "message_id": "rocky-vm-1-3601"
}
```

**These are logs ABOUT publishing/receiving, not the actual message payload.**

### 2. NATS Message Payloads (What flows through NATS)

The actual data being sent:

```json
{
  "message_id": "rocky-vm-1-3601",
  "timestamp": "2025-10-09T01:48:15.988Z",
  "source": "rocky-vm-1",
  "sequence": 3601,
  "data": {
    "event_type": "user.login",
    "value": 42,
    "status": "success"
  }
}
```

**These messages flow through NATS but aren't logged anywhere by default.**

## Solution: Deploy Message Logger

The **MessageLogger** service subscribes to NATS topics and logs the full message payload.

### Option A: Log All Messages (Wildcard)

Add to `docker-compose.yml`:

```yaml
  message-logger:
    build: ./MessageLogger
    container_name: nats-message-logger
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=*.*              # All two-token subjects
      - HOSTNAME=message-logger
    depends_on:
      - nats
    labels:
      - "language=csharp"
      - "component=logger"
      - "purpose=audit"
    restart: unless-stopped
```

**NATS Wildcard Options:**
- `*.*` - Matches `events.test`, `metrics.cpu`, etc. (two tokens)
- `events.*` - Matches all `events.X` subjects
- `events.>` - Matches `events.X`, `events.X.Y`, `events.X.Y.Z` (all under events)
- `>` - Matches **everything** (use carefully!)

### Option B: Log Specific Topics

Log only certain topics:

```yaml
  logger-events:
    build: ./MessageLogger
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=events.>
      - HOSTNAME=logger-events
    labels:
      - "component=logger"
      - "scope=events"

  logger-metrics:
    build: ./MessageLogger
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=metrics.>
      - HOSTNAME=logger-metrics
    labels:
      - "component=logger"
      - "scope=metrics"
```

### Option C: Log from External Applications

If you have **other apps** (not in Docker) publishing to NATS:

```
┌──────────────┐
│ Python App   │────► events.orders.created
│ (external)   │
└──────────────┘
                            ▼
┌──────────────┐        ┌────────────┐
│ Java App     │───────►│    NATS    │
│ (external)   │        │   Server   │
└──────────────┘        └─────┬──────┘
                              │
┌──────────────┐              │
│ Go Service   │──────────────┘
│ (external)   │        events.users.login
└──────────────┘
                              │
                              ▼
                        ┌─────────────┐
                        │   Message   │
                        │   Logger    │─────► Loki
                        └─────────────┘
```

**The MessageLogger will capture all these messages regardless of source.**

## Deployment

### 1. Build and Deploy

```bash
cd ~/nats-pubsub-app/csharp

# Add MessageLogger to docker-compose.yml
cat >> docker-compose.yml <<'EOF'

  message-logger:
    build: ./MessageLogger
    container_name: nats-message-logger
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=events.>
      - HOSTNAME=message-logger
    depends_on:
      - nats
    labels:
      - "language=csharp"
      - "component=logger"
    restart: unless-stopped
EOF

# Build and start
docker compose up -d --build message-logger
```

### 2. Verify It's Working

```bash
# Check logs
docker logs -f nats-message-logger

# You should see:
# {"timestamp":"...","level":"INFO","message":"NATS message captured","subject":"events.test",...}
```

### 3. Query in Grafana

**See all captured messages:**
```logql
{container_name="nats-message-logger"}
```

**See captured messages from specific subject:**
```logql
{container_name="nats-message-logger"} | json | data_subject="events.test"
```

**Extract message payload:**
```logql
{container_name="nats-message-logger"} | json | line_format "{{.data.message}}"
```

## Scaling Considerations

### Low Traffic (< 100 msg/sec)
- Single MessageLogger with wildcard `>` works fine
- Logs all messages to one stream

### Medium Traffic (100-1000 msg/sec)
- Deploy separate loggers per topic namespace
- `logger-events` subscribes to `events.>`
- `logger-metrics` subscribes to `metrics.>`
- Reduces log volume per container

### High Traffic (> 1000 msg/sec)
- Use sampling (log 1 in N messages)
- Use queue groups for load distribution
- Consider JetStream with persistence instead

## Message Logger with Sampling

For high-volume topics, log only a percentage of messages:

**Modify MessageLogger/Program.cs:**

```csharp
private static readonly Random _random = new Random();
private static readonly int _sampleRate = int.Parse(
    Environment.GetEnvironmentVariable("SAMPLE_RATE") ?? "100"
); // Log 1 in N messages

static void SubscribeToMessages()
{
    // ... existing code ...

    var subscription = _connection.SubscribeAsync(_subject, (sender, args) =>
    {
        // Sample messages
        if (_random.Next(_sampleRate) != 0)
        {
            return; // Skip this message
        }

        // ... rest of handler ...
    });
}
```

**Deploy with sampling:**
```yaml
  message-logger:
    environment:
      - SAMPLE_RATE=10  # Log 1 in 10 messages
```

## Querying Message Content

Once messages are in Loki, you can query the actual payload:

**Find all login events:**
```logql
{container_name="nats-message-logger"} | json | data_message=~".*user.login.*"
```

**Count messages by event type:**
```logql
sum by (event_type) (
  count_over_time(
    {container_name="nats-message-logger"} | json [5m]
  )
)
```

**Find errors in message payloads:**
```logql
{container_name="nats-message-logger"} | json | data_message=~".*error.*"
```

**Average message size:**
```logql
avg_over_time(
  {container_name="nats-message-logger"} | json | unwrap data_size_bytes [5m]
)
```

## Comparison: Subscriber vs. MessageLogger

### Your Current Subscriber

**Purpose:** Process messages and do work
- Receives messages
- Validates/transforms data
- Updates database
- Calls APIs
- **Incidentally logs** what it receives

### MessageLogger

**Purpose:** Audit trail and observability
- Receives messages
- **Only logs** the raw payload
- No processing
- No side effects
- Pure observability

**You can run both!**

```yaml
services:
  subscriber:        # Does work
    build: ./Subscriber
    environment:
      - NATS_SUBJECT=events.orders

  message-logger:    # Audits everything
    build: ./MessageLogger
    environment:
      - NATS_SUBJECT=events.>
```

## External Application Example

If you have a Python app publishing to NATS:

```python
# external_app.py
import nats
import asyncio
import json

async def publish_events():
    nc = await nats.connect("nats://192.168.1.131:4222")

    event = {
        "user_id": "12345",
        "action": "purchase",
        "amount": 99.99
    }

    # Publish to NATS
    await nc.publish("events.purchases", json.dumps(event).encode())

    await nc.close()

asyncio.run(publish_events())
```

**Your MessageLogger will automatically capture this** if it's subscribed to `events.>` or `>`.

**Query in Grafana:**
```logql
{container_name="nats-message-logger"} | json | data_message=~".*purchase.*"
```

## Best Practices

### 1. Separate Loggers by Purpose

```yaml
# Audit logger - captures everything for compliance
logger-audit:
  environment:
    - NATS_SUBJECT=events.>

# Debug logger - high-verbosity for troubleshooting
logger-debug:
  environment:
    - NATS_SUBJECT=debug.>

# Metrics logger - only metrics topics
logger-metrics:
  environment:
    - NATS_SUBJECT=metrics.>
```

### 2. Use Labels for Filtering

Add labels to MessageLogger container:

```yaml
labels:
  - "component=logger"
  - "scope=audit"        # Custom label
  - "retention=30d"      # Custom label
```

Query in Grafana:
```logql
{component="logger", scope="audit"}
```

### 3. Control Log Volume

**Option 1: Filter by subject prefix**
```yaml
- NATS_SUBJECT=events.critical.>  # Only critical events
```

**Option 2: Sample messages**
```yaml
- SAMPLE_RATE=100  # Log 1%
```

**Option 3: Log only errors**
Modify MessageLogger to only log messages containing error indicators.

## Summary

1. **Application logs** (what you have): Apps log their own activity
2. **NATS messages** (new): Actual data flowing through NATS
3. **MessageLogger**: Subscribes to topics and logs message payloads
4. Deploy with wildcards (`events.>`) to capture all messages in a namespace
5. Query in Grafana: `{container_name="nats-message-logger"} | json`
6. Works with any app publishing to NATS, regardless of language/platform
