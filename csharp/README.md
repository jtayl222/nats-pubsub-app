# NATS PubSub - C# Implementations

Multiple .NET 8 implementations of NATS publisher/subscriber with structured JSON logging.

## Components Overview

This directory contains **7 different C# components** serving different purposes:

### 1. Basic Publisher + JetStream Subscriber (Publisher/ + Subscriber/)
- **Publisher Library:** NATS.Client 1.1.8 (legacy, core NATS)
- **Subscriber Library:** NATS.Net 2.4.0 (modern, JetStream)
- **Purpose:** Demonstrate JetStream persistence with legacy publisher compatibility
- **Features:**
  - Fetches last 10 messages on startup
  - Auto-creates JetStream stream if missing
  - Durable consumer for new messages
  - Cross-compatible with legacy publishers
- **Deploy:** `docker-compose up -d publisher subscriber nats`
- **Use Case:** Catch up on missed messages, persist events from legacy publishers

### 2. Payment Monitoring (PaymentPublisher/ + MessageLogger/)
- **Library:** NATS.Client 1.1.8 (legacy)
- **Purpose:** Monitor payment systems via NATS without console access
- **Features:** Error simulation, remote system monitoring
- **Deploy:** `docker-compose up -d payment-publisher payment-monitor nats`
- **Use Case:** Monitor remote servers, capture payment errors
- **Docs:** [`../docs/payment-monitoring-setup.md`](../docs/payment-monitoring-setup.md)

### 3. JetStream Persistence (PaymentPublisher-JetStream/ + MessageLogger-JetStream/)
- **Library:** NATS.Net 2.4.0 (modern)
- **Purpose:** Message persistence and historical replay
- **Features:** Durable consumers, replay, at-least-once delivery
- **Deploy:** Requires stream setup (see docs)
- **Use Case:** Production persistence, catch up after downtime
- **Docs:** [`../docs/upgrade-to-nats-net-2.md`](../docs/upgrade-to-nats-net-2.md)

### 4. HTTP Gateway (NatsHttpGateway/)
- **Library:** NATS.Net 2.4.0 (modern) + ASP.NET Core
- **Purpose:** RESTful HTTP/JSON gateway for NATS JetStream
- **Features:**
  - POST /api/messages/{subject} - Publish to any subject
  - GET /api/messages/{subject}?limit=N - Fetch last N messages
  - Auto-creates JetStream streams
  - Stateless design (scales horizontally)
  - Swagger/OpenAPI documentation
- **Deploy:** `docker-compose up -d nats-http-gateway`
- **Use Case:** Web APIs, webhooks, mobile apps, legacy system integration
- **Docs:** [`NatsHttpGateway/README.md`](NatsHttpGateway/README.md)

## Repository Structure

```
csharp/
├── README.md                       # This file
├── docker-compose.yml              # Orchestrates ALL C# services
├── .env.example
├── Publisher/                      # Basic publisher (NATS.Client 1.x)
│   ├── Dockerfile
│   ├── Publisher.csproj
│   └── Program.cs
├── Subscriber/                     # JetStream subscriber (NATS.Net 2.x)
│   ├── Dockerfile
│   ├── Subscriber.csproj           # Uses NATS.Net 2.4.0
│   └── Program.cs                  # Fetches last 10 msgs + subscribes
├── PaymentPublisher/               # Payment simulator (NATS.Client 1.x)
│   ├── README.md
│   ├── Dockerfile
│   ├── PaymentPublisher.csproj
│   └── Program.cs
├── MessageLogger/                  # Message monitor (NATS.Client 1.x)
│   ├── Dockerfile
│   ├── MessageLogger.csproj
│   └── Program.cs
├── PaymentPublisher-JetStream/     # Payment with persistence (NATS.Net 2.x)
│   ├── Dockerfile
│   ├── PaymentPublisher.csproj
│   └── Program.cs
├── MessageLogger-JetStream/        # Monitor with replay (NATS.Net 2.x)
│   ├── Dockerfile
│   ├── MessageLogger.csproj
│   └── Program.cs
└── NatsHttpGateway/                # HTTP/REST API gateway (ASP.NET Core + NATS.Net 2.x)
    ├── README.md
    ├── Dockerfile
    ├── NatsHttpGateway.csproj
    ├── Program.cs                  # Minimal API endpoints
    ├── appsettings.json
    ├── Services/
    │   └── NatsService.cs          # NATS operations
    └── Models/
        ├── PublishRequest.cs
        └── MessageResponse.cs
```

## Quick Start

### Option 1: Basic Pub/Sub

```bash
# Build and start basic publisher/subscriber
docker-compose up -d --build publisher subscriber nats

# View logs
docker-compose logs -f publisher subscriber
```

### Option 2: Payment Monitoring

```bash
# Build and start payment simulation + monitoring
docker-compose up -d --build payment-publisher payment-monitor nats

# View payment monitor logs (shows errors)
docker-compose logs -f payment-monitor

# Payment publisher has logging disabled (simulates remote server)
docker logs payment-publisher  # (empty - simulates no console access)
```

### Option 3: JetStream with Historical Replay

```bash
# Uncomment jetstream-setup service in docker-compose.yml first

# Build and start JetStream versions
docker-compose up -d --build payment-publisher-js payment-monitor-js nats

# View logs with historical replay
docker-compose logs -f payment-monitor-js
```

## Component Details

### Publisher (Basic)

**What it does:**
- Publishes messages to `events.test` every 2 seconds
- Generates random event types (user.login, order.created, etc.)
- Logs structured JSON to stdout

**Configuration:**
```yaml
environment:
  - NATS_URL=nats://nats:4222
  - NATS_SUBJECT=events.test
  - HOSTNAME=csharp-publisher
  - PUBLISH_INTERVAL=2.0  # seconds
```

**Example log:**
```json
{
  "timestamp": "2025-10-09T01:15:30.123Z",
  "level": "INFO",
  "logger": "nats-publisher",
  "message": "Message published",
  "message_id": "csharp-publisher-42",
  "subject": "events.test",
  "sequence": 42
}
```

### Subscriber (Basic)

**What it does:**
- Subscribes to `events.test` messages
- Tracks message latency
- Logs metrics every 60 seconds

**Configuration:**
```yaml
environment:
  - NATS_URL=nats://nats:4222
  - NATS_SUBJECT=events.test
  - HOSTNAME=csharp-subscriber
  - QUEUE_GROUP=workers  # Optional: for load balancing
```

**Example log:**
```json
{
  "timestamp": "2025-10-09T01:15:30.456Z",
  "level": "INFO",
  "logger": "nats-subscriber",
  "message": "Message received",
  "message_id": "csharp-publisher-42",
  "latency_ms": 2.34
}
```

### PaymentPublisher

**What it does:**
- Simulates credit card payment processing
- Publishes to `payments.credit_card.accepted` (most transactions)
- Publishes to `payments.credit_card.declined` (~1 per minute)
- Logs declined transactions at **ERROR** level
- **Logging disabled** to simulate remote server without console access

**Configuration:**
```yaml
environment:
  - NATS_URL=nats://nats:4222
  - HOSTNAME=payment-publisher
  - PUBLISH_INTERVAL=5.0  # seconds between transactions

logging:
  driver: "none"  # Simulates no console access
```

**Publishes payment messages:**
```json
{
  "transaction_id": "TXN-payment-publisher-42",
  "timestamp": "2025-10-09T01:15:30.789Z",
  "source": "payment-publisher",
  "card_type": "Visa",
  "last_four": "4532",
  "amount": 99.99,
  "currency": "USD",
  "status": "declined",
  "decline_reason": "insufficient_funds",
  "decline_code": "ERR-451"
}
```

**Use case:** Monitor payment failures without accessing remote server logs.

See: [`PaymentPublisher/README.md`](PaymentPublisher/README.md)

### MessageLogger

**What it does:**
- Subscribes to NATS topics (wildcards supported)
- Logs full message payloads to stdout
- Logs declined/failed messages at **ERROR** level
- Generic monitoring tool for any NATS messages

**Configuration:**
```yaml
environment:
  - NATS_URL=nats://nats:4222
  - NATS_SUBJECT=payments.>  # Wildcard: all payments.*
  - HOSTNAME=payment-monitor
```

**Example log (captures PaymentPublisher messages):**
```json
{
  "timestamp": "2025-10-09T01:15:30.789Z",
  "level": "ERROR",
  "logger": "nats-message-logger",
  "message": "Payment transaction declined",
  "data": {
    "subject": "payments.credit_card.declined",
    "transaction_id": "TXN-payment-publisher-42",
    "decline_reason": "insufficient_funds",
    "amount": 99.99,
    "payload": { ...full NATS message... }
  }
}
```

**Use case:** Monitor remote systems via NATS when you can't access application logs.

### PaymentPublisher-JetStream

**What it does:**
- Same as PaymentPublisher but uses NATS.Net 2.x with JetStream
- **Messages persisted to disk**
- Returns sequence numbers and acknowledgments
- Auto-creates PAYMENTS stream

**Configuration:**
```yaml
environment:
  - NATS_URL=nats://nats:4222
  - HOSTNAME=payment-publisher-js
  - PUBLISH_INTERVAL=5.0
```

**Key difference:** Messages stored in JetStream stream, survives restarts.

See: [`../docs/upgrade-to-nats-net-2.md`](../docs/upgrade-to-nats-net-2.md)

### MessageLogger-JetStream

**What it does:**
- Same as MessageLogger but uses NATS.Net 2.x with JetStream consumers
- **Replays historical messages** from JetStream streams
- Durable consumer survives restarts
- Resumes from last acknowledged message

**Configuration:**
```yaml
environment:
  - NATS_URL=nats://nats:4222
  - STREAM_NAME=PAYMENTS
  - CONSUMER_NAME=payment-monitor
  - HOSTNAME=payment-monitor-js
  - REPLAY_HISTORY=true  # Replay all historical messages!
```

**Key difference:** Can replay ALL messages from beginning of stream, not just new messages.

**Use case:** Catch up on messages missed during downtime.

See: [`../docs/jetstream-historical-replay.md`](../docs/jetstream-historical-replay.md)

## Deployment Scenarios

### Scenario 1: Learning NATS

```bash
# Deploy basic pub/sub
docker-compose up -d publisher subscriber nats

# View message flow
docker-compose logs -f
```

### Scenario 2: Monitor Remote Payment System

```bash
# On remote server (no console access needed)
docker-compose up -d payment-publisher nats

# On monitoring server
docker-compose up -d payment-monitor

# View payment errors in Loki/Grafana
# Query: {container_name="payment-monitor", level="ERROR"}
```

### Scenario 3: Production with Message Persistence

```bash
# Uncomment jetstream-setup in docker-compose.yml

# Deploy JetStream versions
docker-compose up -d payment-publisher-js payment-monitor-js nats

# Messages persist to disk
# Monitor can replay history if it crashes
```

### Scenario 4: Mixed Deployment (Test All Components)

```bash
# Start everything
docker-compose up -d --build

# You'll have:
# - Basic pub/sub on events.test
# - Payment simulation on payments.*
# - Multiple monitors capturing different topics
```

## Library Comparison

| Feature | NATS.Client 1.1.8 | NATS.Net 2.4.0 |
|---------|-------------------|----------------|
| **Components** | Publisher, Subscriber, PaymentPublisher, MessageLogger | PaymentPublisher-JS, MessageLogger-JS |
| **Persistence** | ❌ No | ✅ Yes (JetStream) |
| **Historical Replay** | ❌ No | ✅ Yes |
| **Durable Consumers** | ❌ No | ✅ Yes |
| **At-Least-Once** | ❌ No | ✅ Yes |
| **API Style** | Callback-based | Modern async/await |
| **Status** | Legacy (maintained) | Modern (active development) |

## Features

### Common Features (All Components)

- ✅ Structured JSON logging for Loki
- ✅ Automatic reconnection
- ✅ Error handling
- ✅ Docker support
- ✅ Alpine-based images (~100-120 MB)
- ✅ Non-root containers
- ✅ Health checks

### NATS.Client 1.x Components

- Simple API
- Callback-based subscriptions
- Fire-and-forget publishing
- No persistence (messages lost if no subscriber)

### NATS.Net 2.x Components

- Modern async/await API
- JetStream persistence
- Durable consumers
- Historical replay
- At-least-once delivery
- Message acknowledgments

## Configuration

### Environment Variables (Common)

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_URL` | `nats://localhost:4222` | NATS server URL |
| `HOSTNAME` | (component name) | Container hostname |

### Publisher-Specific

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_SUBJECT` | `events.test` | Subject to publish to |
| `PUBLISH_INTERVAL` | `2.0` | Seconds between messages |

### Subscriber-Specific

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_SUBJECT` | `events.test` | Subject to subscribe to |
| `QUEUE_GROUP` | (none) | Optional queue group for load balancing |

### MessageLogger-Specific

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_SUBJECT` | `>` | Subject filter (supports wildcards) |

### JetStream-Specific

| Variable | Default | Description |
|----------|---------|-------------|
| `STREAM_NAME` | `PAYMENTS` | JetStream stream name |
| `CONSUMER_NAME` | `payment-monitor` | Durable consumer name |
| `REPLAY_HISTORY` | `true` | Replay historical messages |

## Querying Logs in Grafana

### Basic Pub/Sub

```logql
# All basic publisher logs
{container_name="csharp-publisher"}

# All basic subscriber logs
{container_name="csharp-subscriber"}

# Publish rate
rate({container_name="csharp-publisher"} |= "Message published" [1m])

# Average latency
avg_over_time({container_name="csharp-subscriber"} | json | unwrap latency_ms [5m])
```

### Payment Monitoring

```logql
# All payment errors (declined transactions)
{container_name="payment-monitor", level="ERROR"}

# Declined transactions with details
{container_name="payment-monitor", level="ERROR"}
| json
| line_format "{{.data.decline_reason}}: ${{.data.amount}}"

# Count by decline reason
sum by (decline_reason) (
  count_over_time(
    {container_name="payment-monitor", level="ERROR"} | json [1h]
  )
)
```

### JetStream

```logql
# JetStream publisher with sequence numbers
{container_name="payment-publisher-js"} | json | line_format "Seq {{.data.js_sequence}}"

# Historical replay
{container_name="payment-monitor-js"} | json | line_format "Processing seq {{.data.js_sequence}}"
```

## Docker Compose

All components are defined in a single `docker-compose.yml`:

```bash
# Start specific components
docker-compose up -d publisher subscriber nats

# Start payment monitoring
docker-compose up -d payment-publisher payment-monitor nats

# Start JetStream versions
docker-compose up -d payment-publisher-js payment-monitor-js nats

# Start everything
docker-compose up -d

# View logs
docker-compose logs -f <service-name>

# Stop all
docker-compose down
```

## Troubleshooting

### Publisher Not Connecting

```bash
# Check NATS is running
docker logs nats-server-csharp

# Check publisher logs
docker logs csharp-publisher

# Verify network
docker exec csharp-publisher nc -zv nats 4222
```

### Subscriber Not Receiving Messages

```bash
# Verify subjects match
docker logs csharp-publisher | head -1 | jq .subject
docker logs csharp-subscriber | head -1 | jq .subject

# Check subscriber is running
docker logs csharp-subscriber
```

### Payment Publisher Has No Logs

This is **expected** - logging is disabled to simulate remote server without console access.

**Verify it's working:**
```bash
# Check container is running
docker ps | grep payment-publisher

# Check payment-monitor logs instead
docker logs payment-monitor
# Should show payment transactions being captured
```

### JetStream Stream Not Found

```bash
# Check if jetstream-setup ran
docker logs jetstream-setup

# Manually create stream
docker exec -it nats-server-csharp sh
nats stream add PAYMENTS \
  --subjects "payments.>" \
  --storage file \
  --retention limits \
  --max-age 168h
```

### MessageLogger Not Replaying History

**Check REPLAY_HISTORY setting:**
```bash
docker exec payment-monitor-js env | grep REPLAY_HISTORY
# Should output: REPLAY_HISTORY=true
```

**Reset consumer:**
```bash
docker exec -it nats-server-csharp sh
nats consumer rm PAYMENTS payment-monitor
docker-compose restart payment-monitor-js
```

## Development

### Local Development (Without Docker)

```bash
# Install .NET 8 SDK
# https://dotnet.microsoft.com/download

# Build publisher
cd Publisher
dotnet restore
dotnet build

# Run publisher
export NATS_URL=nats://localhost:4222
export NATS_SUBJECT=events.test
dotnet run

# In another terminal, build and run subscriber
cd ../Subscriber
dotnet restore
dotnet build
dotnet run
```

### Building Docker Images

```bash
# Build specific component
docker-compose build publisher

# Build JetStream versions
docker-compose build payment-publisher-js payment-monitor-js

# Build all
docker-compose build
```

## Cross-Language Testing

Test C# ↔ Python communication:

```bash
# Start C# publisher
docker-compose up -d publisher nats

# Start Python subscriber (from ../python)
cd ../python
docker-compose up -d subscriber

# View Python subscriber receiving C# messages
docker logs -f nats-subscriber | grep "csharp"
```

## Performance

| Component | Memory | CPU (idle) | Throughput |
|-----------|--------|------------|------------|
| Publisher | ~100 MB | <0.1 core | ~2K msg/s |
| Subscriber | ~100 MB | <0.1 core | ~2K msg/s |
| PaymentPublisher | ~100 MB | <0.1 core | ~500 tx/s |
| MessageLogger | ~100 MB | <0.1 core | ~1K msg/s |
| PaymentPublisher-JS | ~120 MB | <0.1 core | ~1.5K msg/s |
| MessageLogger-JS | ~120 MB | <0.1 core | ~1K msg/s |

## Versions

- **NATS Server:** 2.10.7-alpine
- **.NET:** 8.0
- **NATS.Client:** 1.1.8 (Publisher, Subscriber, PaymentPublisher, MessageLogger)
- **NATS.Net:** 2.4.0 (PaymentPublisher-JetStream, MessageLogger-JetStream)
- **System.Text.Json:** 8.0.5

## Documentation

### Component-Specific
- [`PaymentPublisher/README.md`](PaymentPublisher/README.md) - Payment publisher details

### Shared Documentation
- [`../docs/component-overview.md`](../docs/component-overview.md) - Component guide
- [`../docs/payment-monitoring-setup.md`](../docs/payment-monitoring-setup.md) - Payment monitoring setup
- [`../docs/message-logging-guide.md`](../docs/message-logging-guide.md) - Message logging guide
- [`../docs/upgrade-to-nats-net-2.md`](../docs/upgrade-to-nats-net-2.md) - NATS.Net 2.x upgrade
- [`../docs/jetstream-historical-replay.md`](../docs/jetstream-historical-replay.md) - JetStream guide

## Support

For issues:
- Check component-specific README files
- Review [`../docs/component-overview.md`](../docs/component-overview.md)
- Check troubleshooting sections in relevant docs
- Review Docker logs: `docker logs <container-name>`
