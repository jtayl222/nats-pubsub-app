# NATS Subjects Guide

How to use multiple NATS subjects/topics and query them in Loki/Grafana.

## Understanding NATS Subjects

NATS subjects are like Kafka topics - they're **hierarchical routing paths** for messages.

### Subject Hierarchy

NATS subjects use dot-notation for hierarchy:

```
events.orders.created
events.orders.updated
events.users.login
events.users.logout
metrics.cpu.usage
metrics.memory.usage
```

### Wildcards

NATS supports wildcards for subscriptions:
- `*` - Matches one token: `events.*.created` matches `events.orders.created` and `events.users.created`
- `>` - Matches one or more tokens: `events.>` matches everything under `events.`

## Current Architecture

### Default Subject
Both C# and Python implementations use:
```
events.test
```

### How Subjects Flow to Loki

```
┌──────────────┐                 ┌──────────────┐
│  Publisher   │────subject────► │     NATS     │
│              │  "events.test"  │    Server    │
└──────────────┘                 └──────────────┘
       │                                │
       │ logs {subject: "events.test"}  │
       ▼                                ▼
┌──────────────┐                 ┌──────────────┐
│   Promtail   │                 │  Subscriber  │
│  (extracts   │                 │              │
│   subject    │                 └──────────────┘
│   as label)  │                        │
└──────┬───────┘                        │ logs {subject: "events.test"}
       │                                │
       └────────────┬───────────────────┘
                    ▼
             ┌─────────────┐
             │    Loki     │
             │ Labels:     │
             │ - subject   │
             │ - level     │
             │ - logger    │
             └─────────────┘
```

## Adding Multiple Subjects

### Scenario 1: Different Publishers for Different Subjects

Deploy multiple publisher instances with different subjects:

```yaml
# docker-compose.yml
services:
  nats:
    image: nats:2.10.7-alpine
    ports:
      - "4222:4222"
      - "8222:8222"
    volumes:
      - ../nats-config/nats-server-standalone.conf:/etc/nats/nats-server.conf
    command: ["-c", "/etc/nats/nats-server.conf"]

  publisher-orders:
    build: ./Publisher
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=events.orders
      - HOSTNAME=publisher-orders
      - PUBLISH_INTERVAL=2.0
    labels:
      - "language=csharp"
      - "component=publisher"
      - "subject=events.orders"

  publisher-users:
    build: ./Publisher
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=events.users
      - HOSTNAME=publisher-users
      - PUBLISH_INTERVAL=3.0
    labels:
      - "language=csharp"
      - "component=publisher"
      - "subject=events.users"

  publisher-metrics:
    build: ./Publisher
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=metrics.system
      - HOSTNAME=publisher-metrics
      - PUBLISH_INTERVAL=5.0
    labels:
      - "language=csharp"
      - "component=publisher"
      - "subject=metrics.system"

  # Wildcard subscriber - receives all events.*
  subscriber-events:
    build: ./Subscriber
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=events.>
      - HOSTNAME=subscriber-events
    labels:
      - "language=csharp"
      - "component=subscriber"
      - "subject=events.*"

  # Specific subscriber - only receives metrics
  subscriber-metrics:
    build: ./Subscriber
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=metrics.>
      - HOSTNAME=subscriber-metrics
    labels:
      - "language=csharp"
      - "component=subscriber"
      - "subject=metrics.*"
```

Deploy:
```bash
docker compose up -d
```

### Scenario 2: Dynamic Subject per Message

Modify the Publisher to publish to different subjects based on event type:

**Publisher/Program.cs** - Update `PublishLoop()`:

```csharp
static async Task PublishLoop()
{
    LogInfo("Starting publish loop", new { interval_seconds = _publishInterval });

    while (true)
    {
        try
        {
            if (_connection == null || !_connection.State.Equals(ConnState.CONNECTED))
            {
                LogWarning("Not connected to NATS, waiting...");
                await Task.Delay(1000);
                continue;
            }

            _messageCount++;

            var eventTypes = new[] { "UserCreated", "OrderPlaced", "PaymentProcessed", "ShipmentSent" };
            var eventType = eventTypes[_messageCount % eventTypes.Length];

            var message = new MessageData
            {
                MessageId = $"{_hostname}-{_messageCount}",
                Timestamp = DateTime.UtcNow.ToString("o"),
                Source = _hostname,
                Sequence = _messageCount,
                Data = new MessagePayload
                {
                    EventType = eventType,
                    Value = Random.Shared.Next(1, 100),
                    Status = _messageCount % 2 == 0 ? "success" : "pending",
                    Description = $"Event {eventType} #{_messageCount}"
                }
            };

            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Determine subject based on event type
            string subject = eventType switch
            {
                "UserCreated" => "events.users.created",
                "OrderPlaced" => "events.orders.placed",
                "PaymentProcessed" => "events.payments.processed",
                "ShipmentSent" => "events.shipments.sent",
                _ => "events.unknown"
            };

            _connection?.Publish(subject, bytes);

            LogInfo("Message published", new
            {
                message_id = message.MessageId,
                subject = subject,  // Log the actual subject used
                size_bytes = bytes.Length,
                sequence = _messageCount,
                event_type = message.Data.EventType
            });

            if (_messageCount % 50 == 0)
            {
                LogInfo("Publisher metrics", new
                {
                    total_messages = _messageCount,
                    uptime_seconds = (DateTime.UtcNow - _startTime).TotalSeconds,
                    messages_per_second = _messageCount / (DateTime.UtcNow - _startTime).TotalSeconds
                });
            }

            await Task.Delay(TimeSpan.FromSeconds(_publishInterval));
        }
        catch (Exception ex)
        {
            LogError("Publish error", ex);
            await Task.Delay(5000);
        }
    }
}
```

### Scenario 3: Subject Namespacing by Environment

Use environment-specific subject prefixes:

```yaml
# Production
environment:
  - NATS_SUBJECT=prod.events.orders

# Staging
environment:
  - NATS_SUBJECT=staging.events.orders

# Development
environment:
  - NATS_SUBJECT=dev.events.orders
```

## Querying Subjects in Loki/Grafana

Once you've updated Promtail (using the updated configs), subjects become **queryable labels**.

### Update Promtail on Your VMs

**Option 1: Re-run Install Script**
```bash
# On each NATS VM (rocky-vm-1, rocky-vm-2)
sudo systemctl stop promtail
sudo ./install-promtail.sh 192.168.1.133
```

**Option 2: Manual Update**
```bash
# On each NATS VM
sudo vi /etc/promtail/config.yaml
# Add to the json expressions section:
#   subject: subject
#   logger: logger
#   event_type: event_type
# Add to the labels section:
#   subject:
#   logger:
#   event_type:

sudo systemctl restart promtail
```

### Reset Promtail Position (Important!)

After config changes, reset Promtail to re-process logs:

```bash
sudo systemctl stop promtail
sudo rm /tmp/positions.yaml
sudo systemctl start promtail
```

### Grafana LogQL Queries

**All logs from specific subject:**
```logql
{subject="events.test"}
```

**Wildcard subject matching:**
```logql
{subject=~"events.*"}
```

**Orders subject only:**
```logql
{subject="events.orders.placed"}
```

**All user events:**
```logql
{subject=~"events.users.*"}
```

**Combine multiple filters:**
```logql
{subject="events.orders", level="ERROR"}
```

**Message rate by subject:**
```logql
rate({job="docker"} | json [1m]) by (subject)
```

**Count messages per subject:**
```logql
count_over_time({job="docker"} | json [1h]) by (subject)
```

**Show subjects with errors:**
```logql
{level="ERROR"} | json | line_format "{{.subject}}: {{.message}}"
```

**Average latency by subject:**
```logql
avg_over_time({container_name="csharp-subscriber"} | json | unwrap latency_ms [5m]) by (subject)
```

### Available Labels After Update

Once Promtail is updated, you'll have these labels:

```
job="docker"
container_name="csharp-publisher"
host="rocky-vm-1"
language="csharp"
component="publisher"
level="INFO"
subject="events.orders"        ← NEW
logger="nats-publisher"        ← NEW
event_type="UserCreated"       ← NEW
```

## Best Practices

### 1. Subject Naming Convention

Use hierarchical naming:
```
<category>.<resource>.<action>

Examples:
events.orders.created
events.orders.updated
events.users.login
events.users.logout
metrics.cpu.usage
metrics.memory.free
alerts.system.disk_full
```

### 2. Avoid Label Cardinality Issues

**Problem:** Too many unique subject values creates performance issues in Loki.

**Bad Example (High Cardinality):**
```
events.orders.123456789  # Order ID in subject
events.users.john-smith  # Username in subject
```

**Good Example (Low Cardinality):**
```
events.orders.created   # Generic action
events.users.login      # Generic action
# Put IDs in the log message, not the subject
```

**Loki Limitation:** Keep total unique label combinations under 10,000.

### 3. Use Hierarchies for Filtering

```
events.>              # All events
events.orders.>       # All order events
events.orders.created # Specific event

metrics.>             # All metrics
metrics.cpu.>         # All CPU metrics
```

### 4. Separate Concerns

```
events.*    → Application events (user actions, orders, etc.)
metrics.*   → System/application metrics
logs.*      → General application logs
alerts.*    → Alert/notification events
commands.*  → Command/request handling
queries.*   → Query/read operations
```

## Example: Multi-Subject Deployment

Complete example with 3 subjects:

```bash
# Deploy
cd ~/nats-pubsub-app/csharp
cat > docker-compose-multi.yml <<'EOF'
version: '3.8'

services:
  nats:
    image: nats:2.10.7-alpine
    container_name: nats-server
    ports:
      - "4222:4222"
      - "8222:8222"
    volumes:
      - ../nats-config/nats-server-standalone.conf:/etc/nats/nats-server.conf
    command: ["-c", "/etc/nats/nats-server.conf"]
    labels:
      - "language=none"
      - "component=messaging"

  pub-orders:
    build: ./Publisher
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=events.orders
      - HOSTNAME=pub-orders
    labels:
      - "language=csharp"
      - "component=publisher"

  pub-users:
    build: ./Publisher
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=events.users
      - HOSTNAME=pub-users
    labels:
      - "language=csharp"
      - "component=publisher"

  pub-metrics:
    build: ./Publisher
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=metrics.system
      - HOSTNAME=pub-metrics
    labels:
      - "language=csharp"
      - "component=publisher"

  sub-all:
    build: ./Subscriber
    environment:
      - NATS_URL=nats://nats:4222
      - NATS_SUBJECT=events.>
      - HOSTNAME=sub-all
    labels:
      - "language=csharp"
      - "component=subscriber"
EOF

docker compose -f docker-compose-multi.yml up -d
```

### Query in Grafana

```logql
# See all subjects
{job="docker"} | json | line_format "{{.subject}}"

# Group by subject
sum by (subject) (count_over_time({job="docker"}[5m]))

# Orders only
{subject="events.orders"}

# All events (not metrics)
{subject=~"events.*"}
```

## Troubleshooting

### Subject Not Appearing as Label

**Check Promtail config:**
```bash
sudo cat /etc/promtail/config.yaml | grep -A5 "json:"
sudo cat /etc/promtail/config.yaml | grep -A5 "labels:"
```

**Verify subject in logs:**
```bash
docker logs csharp-publisher | head -5 | jq .subject
```

**Reset Promtail:**
```bash
sudo systemctl stop promtail
sudo rm /tmp/positions.yaml
sudo systemctl start promtail
```

### High Cardinality Warning

If you see "maximum number of streams" errors in Loki:

**Check cardinality:**
```bash
curl http://localhost:3100/loki/api/v1/label/subject/values | jq
```

**Solution:** Reduce number of unique subjects or increase Loki limits in `loki-config.yaml`:
```yaml
limits_config:
  max_streams_per_user: 10000  # Increase if needed
```

## Summary

- NATS subjects are already in your logs as JSON fields
- Updated Promtail configs extract `subject`, `logger`, `event_type` as Loki labels
- Re-run install script or manually update `/etc/promtail/config.yaml` on VMs
- Reset Promtail position file after config changes
- Query by subject in Grafana: `{subject="events.orders"}`
- Use hierarchical subjects: `events.orders.created`
- Avoid high cardinality (user IDs, order IDs in subjects)
