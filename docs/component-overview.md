# Component Overview & Use Cases

Complete guide to all NATS PubSub components and when to use each one.

## Component Matrix

| Component | Language | Library | Persistence | Primary Use Case |
|-----------|----------|---------|-------------|------------------|
| **Publisher** | C# | NATS.Client 1.1.8 | No | Learning NATS basics |
| **Subscriber** | C# | NATS.Client 1.1.8 | No | Learning NATS basics |
| **PaymentPublisher** | C# | NATS.Client 1.1.8 | No | Simulate payment errors for monitoring |
| **MessageLogger** | C# | NATS.Client 1.1.8 | No | Monitor remote systems without console access |
| **PaymentPublisher-JetStream** | C# | NATS.Net 2.4.0 | ✅ Yes | Production payment processing with persistence |
| **MessageLogger-JetStream** | C# | NATS.Net 2.4.0 | ✅ Yes | Audit trail with historical replay |
| **publisher.py** | Python | nats-py 2.6.0 | No | Learning, cross-language testing |
| **subscriber.py** | Python | nats-py 2.6.0 | No | Learning, cross-language testing |

## When to Use Each Component

### Scenario 1: Learning NATS

**Goal:** Understand how NATS pub/sub works

**Deploy:**
- Python: `publisher.py` + `subscriber.py`
- OR C#: `Publisher/` + `Subscriber/`

**Why:**
- Simple, minimal setup
- No persistence complexity
- Cross-language compatible for learning

**Documentation:**
- [Python README](../python/README.md)
- [C# README](../csharp/README.md)

---

### Scenario 2: Monitor Remote Payment System

**Goal:** Monitor a remote server without console access, only via NATS topics

**Deploy:**
- Remote server: `PaymentPublisher/` (with `logging: "none"`)
- Monitoring server: `MessageLogger/`

**Why:**
- PaymentPublisher simulates credit card transactions
- Randomly generates declined transactions (~1/minute)
- MessageLogger captures messages via NATS and logs to Loki
- No need for console access to remote server

**Use Cases:**
- Production server in locked-down environment
- Compliance monitoring without direct access
- Centralized error tracking across distributed systems

**Documentation:**
- [Payment Monitoring Setup](payment-monitoring-setup.md)
- [Message Logging Guide](message-logging-guide.md)

---

### Scenario 3: Production with Message Persistence

**Goal:** Ensure no messages are lost, even during downtime

**Deploy:**
- `PaymentPublisher-JetStream/`
- `MessageLogger-JetStream/`

**Why:**
- JetStream stores messages to disk
- Durable consumers track processing position
- Automatic replay of missed messages
- At-least-once delivery guarantees

**Use Cases:**
- Payment processing (cannot lose transactions)
- Audit trails (must capture everything)
- Critical event tracking

**Documentation:**
- [JetStream Historical Replay](jetstream-historical-replay.md)
- [Upgrade to NATS.Net 2.x](upgrade-to-nats-net-2.md)

---

### Scenario 4: Catch Up After Downtime

**Goal:** Monitor was offline, need to see what happened while it was down

**Deploy:**
- `MessageLogger-JetStream/` with `REPLAY_HISTORY=true`

**Why:**
- Replays ALL messages from stream beginning
- Or resumes from last acknowledged message
- No data loss during downtime

**Use Cases:**
- Monitor crashed and restarted
- Scheduled maintenance window
- Investigating historical issues

**Documentation:**
- [JetStream Historical Replay](jetstream-historical-replay.md)

---

### Scenario 5: Cross-Language Testing

**Goal:** Test message compatibility between Python and C#

**Deploy:**
- Python publisher + C# subscriber
- OR C# publisher + Python subscriber

**Why:**
- Verify message schema compatibility
- Test language-agnostic messaging
- Validate JSON serialization

**Documentation:**
- [Message Format Specification](message-format.md)
- [Python README](../python/README.md)

---

## Component Dependencies

```
NATS Server (required by all)
    │
    ├─► Python Pub/Sub (standalone)
    │   ├── publisher.py
    │   └── subscriber.py
    │
    ├─► C# Basic Pub/Sub (standalone)
    │   ├── Publisher/
    │   └── Subscriber/
    │
    ├─► Payment Monitoring (NATS.Client 1.x)
    │   ├── PaymentPublisher/ ──► MessageLogger/ (optional)
    │   │
    │   │   MessageLogger can run without PaymentPublisher
    │   │   (monitors any NATS subject)
    │
    └─► JetStream Stream (persistence layer)
            │
            ├─► PaymentPublisher-JetStream/
            │   (creates stream automatically)
            │
            └─► MessageLogger-JetStream/
                (consumes from stream with replay)
```

## Migration Path

### Phase 1: Learning
1. Start with Python or C# basic pub/sub
2. Understand NATS subjects and message format
3. Test cross-language communication

### Phase 2: Add Monitoring
1. Deploy MessageLogger to capture messages
2. Query messages in Loki/Grafana
3. Set up alerts on ERROR level logs

### Phase 3: Add Domain Logic
1. Deploy PaymentPublisher for specific use case
2. Simulate errors for testing monitoring
3. Monitor remote systems via NATS topics

### Phase 4: Add Persistence
1. Migrate to JetStream versions (-JetStream/ directories)
2. Upgrade from NATS.Client 1.x to NATS.Net 2.x
3. Configure stream retention and replay policies

### Phase 5: Production
1. Use JetStream for message durability
2. Set up durable consumers
3. Enable historical replay for troubleshooting

## Feature Comparison

### Basic Pub/Sub (Publisher/ + Subscriber/)

**Features:**
- ✅ Simple message publishing
- ✅ Basic subscription
- ✅ Cross-language compatible
- ✅ Low resource usage
- ❌ No persistence
- ❌ No historical replay
- ❌ Messages lost if subscriber offline

**Best for:** Learning, demos, non-critical messaging

---

### Payment Monitoring (PaymentPublisher/ + MessageLogger/)

**Features:**
- ✅ Credit card transaction simulation
- ✅ Random error generation (~1/minute)
- ✅ Remote monitoring without console access
- ✅ ERROR level logging for declined transactions
- ✅ Generic message capture (any NATS subject)
- ❌ No persistence
- ❌ No historical replay

**Best for:** Monitoring remote systems, error tracking, testing alerting

---

### JetStream Persistence (PaymentPublisher-JetStream/ + MessageLogger-JetStream/)

**Features:**
- ✅ Message persistence to disk
- ✅ Historical replay from stream beginning
- ✅ Durable consumers (resume from last ack)
- ✅ At-least-once delivery
- ✅ Automatic stream creation
- ✅ Configurable retention (time/size limits)
- ✅ Modern NATS.Net 2.x library
- ⚠️ Requires more setup (stream configuration)
- ⚠️ Higher resource usage (disk storage)

**Best for:** Production, critical messaging, audit trails

---

## Quick Start Commands

### Learning NATS (Python)
```bash
cd python
docker-compose up -d --build
docker-compose logs -f
```

### Learning NATS (C#)
```bash
cd csharp
docker-compose up -d --build publisher subscriber nats
docker-compose logs -f publisher subscriber
```

### Payment Monitoring (C#)
```bash
cd csharp
docker-compose up -d --build payment-publisher payment-monitor nats
docker-compose logs -f payment-monitor
```

### JetStream with Historical Replay (C#)
```bash
cd csharp
# Uncomment jetstream-setup service in docker-compose.yml first
docker-compose up -d --build payment-publisher-js payment-monitor-js nats
docker-compose logs -f payment-monitor-js
```

---

## Grafana Query Examples

### All Python logs
```logql
{container_name=~"nats-(publisher|subscriber)"}
```

### All C# logs
```logql
{container_name=~"csharp-.*"}
```

### Payment errors only
```logql
{container_name="payment-monitor", level="ERROR"}
```

### Specific NATS subject
```logql
{container_name="csharp-subscriber"} | json | subject="events.test"
```

### Compare publish rates (all publishers)
```logql
sum by (container_name) (
  rate({container_name=~"(nats-publisher|csharp-publisher|payment-publisher.*)"}
  |= "Message published" [1m])
)
```

### JetStream sequence tracking
```logql
{container_name="payment-monitor-js"} | json | line_format "Seq: {{.js_sequence}} - {{.transaction_id}}"
```

---

## Environment Variables

### Common (All Components)

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_URL` | `nats://localhost:4222` | NATS server URL |
| `HOSTNAME` | Component name | Hostname for logging |

### Publisher Components

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_SUBJECT` | `events.test` | Subject to publish to |
| `PUBLISH_INTERVAL` | `2.0` | Seconds between messages |

### Subscriber Components

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_SUBJECT` | `events.test` | Subject to subscribe to |
| `QUEUE_GROUP` | (none) | Queue group for load balancing |

### PaymentPublisher

| Variable | Default | Description |
|----------|---------|-------------|
| `PUBLISH_INTERVAL` | `5.0` | Seconds between transactions |
| (Subjects hardcoded) | `payments.credit_card.accepted`<br>`payments.credit_card.declined` | Payment subjects |

### MessageLogger

| Variable | Default | Description |
|----------|---------|-------------|
| `NATS_SUBJECT` | `payments.>` | Subject wildcard to monitor |

### JetStream Components

| Variable | Default | Description |
|----------|---------|-------------|
| `STREAM_NAME` | `PAYMENTS` | JetStream stream name |
| `CONSUMER_NAME` | `payment-monitor` | Durable consumer name |
| **`REPLAY_HISTORY`** | `true` | Replay all historical messages |

---

## Documentation Map

### Getting Started
- [README.md](../README.md) - Monorepo overview
- [python/README.md](../python/README.md) - Python implementation
- [csharp/README.md](../csharp/README.md) - C# implementations

### Architecture & Design
- [architecture.md](architecture.md) - System architecture
- [message-format.md](message-format.md) - Message schema specification
- **[component-overview.md](component-overview.md)** - This file (component guide)

### Deployment Guides
- [single-vm-deployment.md](single-vm-deployment.md) - Single VM setup
- [payment-monitoring-setup.md](payment-monitoring-setup.md) - Payment monitoring
- [jetstream-historical-replay.md](jetstream-historical-replay.md) - JetStream setup

### Advanced Topics
- [nats-subjects-guide.md](nats-subjects-guide.md) - NATS subjects/topics
- [message-logging-guide.md](message-logging-guide.md) - Message monitoring
- [upgrade-to-nats-net-2.md](upgrade-to-nats-net-2.md) - Library upgrade guide

---

## Troubleshooting by Component

### Python Publisher/Subscriber
See [python/README.md - Troubleshooting](../python/README.md#troubleshooting)

### C# Basic Pub/Sub
See [csharp/README.md - Troubleshooting](../csharp/README.md#troubleshooting)

### PaymentPublisher/MessageLogger
See [payment-monitoring-setup.md - Troubleshooting](payment-monitoring-setup.md#troubleshooting)

### JetStream Components
See [jetstream-historical-replay.md - Troubleshooting](jetstream-historical-replay.md#troubleshooting)

---

## Summary

| If you need... | Use this... | Library | Docs |
|----------------|-------------|---------|------|
| Learn NATS basics | Python or C# basic pub/sub | nats-py or NATS.Client 1.x | [Python README](../python/README.md) |
| Monitor remote system | PaymentPublisher + MessageLogger | NATS.Client 1.x | [Payment Monitoring](payment-monitoring-setup.md) |
| Message persistence | JetStream versions | NATS.Net 2.x | [JetStream Guide](jetstream-historical-replay.md) |
| Historical replay | MessageLogger-JetStream | NATS.Net 2.x | [JetStream Guide](jetstream-historical-replay.md) |
| Cross-language testing | Python ↔ C# | Any | [Message Format](message-format.md) |

---

## Next Steps

1. **New to NATS?** Start with Python or C# basic pub/sub
2. **Need monitoring?** Deploy PaymentPublisher + MessageLogger
3. **Need persistence?** Upgrade to JetStream versions
4. **Need help?** Check the [documentation map](#documentation-map) above
