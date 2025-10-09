# NATS PubSub Application - Multi-Language Monorepo

Production-ready NATS publisher/subscriber application with multiple language implementations and structured JSON logging for Loki integration.

## Overview

This monorepo contains NATS pub/sub implementations in multiple languages with varying feature sets:

### Python Implementation
- **Basic Pub/Sub Demo** - Simple publisher/subscriber with `nats-py`
- **Purpose:** Learning, cross-language testing
- **Features:** Structured logging, basic messaging

### C# Implementations
Multiple implementations serving different purposes:

1. **Basic Pub/Sub** (NATS.Client 1.1.8)
   - Simple publisher/subscriber demo
   - Cross-language compatible with Python

2. **Payment Monitoring** (NATS.Client 1.1.8)
   - Payment simulation with error generation
   - NATS message monitoring/logging service
   - Monitor remote systems without console access

3. **JetStream with Persistence** (NATS.Net 2.4.0)
   - Message persistence and historical replay
   - Durable consumers with at-least-once delivery
   - Production-ready persistence layer

All implementations:
- Follow the same message schema (see [`docs/message-format.md`](docs/message-format.md))
- Use structured JSON logging for Loki/Promtail
- Are fully interoperable across languages
- Share the same NATS server configuration

## Repository Structure

```
nats-pubsub-app/
├── README.md                           # This file
├── .gitignore
├── nats-config/                        # Shared NATS configuration
│   ├── nats-server.conf                # Clustered setup
│   └── nats-server-standalone.conf     # Single-node setup
├── docs/                               # Shared documentation
│   ├── architecture.md                 # System architecture
│   ├── message-format.md               # Message schema specification
│   ├── component-overview.md           # Component guide and use cases
│   ├── single-vm-deployment.md         # Single VM deployment guide
│   ├── nats-subjects-guide.md          # NATS subjects and topics
│   ├── message-logging-guide.md        # Message monitoring guide
│   ├── payment-monitoring-setup.md     # Payment monitoring setup
│   ├── jetstream-historical-replay.md  # JetStream persistence guide
│   └── upgrade-to-nats-net-2.md        # NATS.Net 2.x migration guide
├── python/                             # Python implementation
│   ├── README.md                       # Python-specific docs
│   ├── docker-compose.yml
│   ├── .env.example
│   ├── publisher/
│   │   ├── Dockerfile
│   │   ├── publisher.py
│   │   └── requirements.txt
│   ├── subscriber/
│   │   ├── Dockerfile
│   │   ├── subscriber.py
│   │   └── requirements.txt
│   └── tests/
│       └── integration_test.sh
└── csharp/                             # C# implementations
    ├── README.md                       # C#-specific docs
    ├── docker-compose.yml              # Orchestrates all C# services
    ├── .env.example
    ├── Publisher/                      # Basic publisher (NATS.Client 1.x)
    │   ├── Dockerfile
    │   ├── Publisher.csproj
    │   └── Program.cs
    ├── Subscriber/                     # Basic subscriber (NATS.Client 1.x)
    │   ├── Dockerfile
    │   ├── Subscriber.csproj
    │   └── Program.cs
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
    └── MessageLogger-JetStream/        # Monitor with replay (NATS.Net 2.x)
        ├── Dockerfile
        ├── MessageLogger.csproj
        └── Program.cs
```

## Quick Start

### Option 1: Python Basic Pub/Sub

```bash
cd python
docker-compose up -d --build
docker-compose logs -f
```

### Option 2: C# Basic Pub/Sub

```bash
cd csharp
docker-compose up -d --build publisher subscriber nats
docker-compose logs -f publisher subscriber
```

### Option 3: Payment Monitoring (C# Only)

```bash
cd csharp
docker-compose up -d --build payment-publisher payment-monitor nats
docker-compose logs -f payment-monitor
```

See [`docs/payment-monitoring-setup.md`](docs/payment-monitoring-setup.md) for complete setup.

### Option 4: JetStream with Historical Replay (C# Only)

```bash
cd csharp
# Uncomment jetstream-setup service in docker-compose.yml
docker-compose up -d --build payment-publisher-js payment-monitor-js nats
docker-compose logs -f payment-monitor-js
```

See [`docs/upgrade-to-nats-net-2.md`](docs/upgrade-to-nats-net-2.md) for complete setup.

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                    NATS Server Cluster                    │
│                    (Shared by all langs)                  │
└─────────────┬────────────────────────────┬───────────────┘
              │                            │
       ┌──────┴──────┐             ┌──────┴──────┐
       │   Python    │             │     C#      │
       │   (Demo)    │             │  (Multiple) │
   ┌───▼────┐   ┌───▼────┐   ┌───▼────┐   ┌───▼────┐
   │Publish │   │Subscribe│  │ Payment│   │ Message│
   │  .py   │   │  .py    │  │  Sim   │   │ Logger │
   └───┬────┘   └───┬────┘   └───┬────┘   └───┬────┘
       │            │             │            │
       └────────────┴─────────────┴────────────┘
                    │
              JSON Logs (stdout)
                    │
                    ▼
              Promtail Agent
                    │
                    ▼
               Loki Server
                    │
                    ▼
            Grafana Dashboard
```

All implementations publish to and subscribe from the same NATS subjects, enabling full cross-language communication.

## Component Use Cases

See [`docs/component-overview.md`](docs/component-overview.md) for detailed component guide.

### When to Use Each Component

| Component | Use Case | Persistence | Library |
|-----------|----------|-------------|---------|
| **Python Pub/Sub** | Learning NATS basics | No | nats-py |
| **C# Pub/Sub** | Learning NATS basics | No | NATS.Client 1.x |
| **PaymentPublisher** | Simulate payment errors for monitoring | No | NATS.Client 1.x |
| **MessageLogger** | Monitor remote systems via NATS | No | NATS.Client 1.x |
| **PaymentPublisher-JS** | Production payment processing | ✅ Yes | NATS.Net 2.x |
| **MessageLogger-JS** | Audit trail with historical replay | ✅ Yes | NATS.Net 2.x |

## Features

### Common Features (All Implementations)

- ✅ Structured JSON logging for Loki
- ✅ Message latency tracking
- ✅ Automatic reconnection and error handling
- ✅ Metrics logging (message rate, error rate, uptime)
- ✅ Docker Compose orchestration
- ✅ NATS clustering support
- ✅ Health checks
- ✅ Production-ready configuration

### Python Implementation

- Async/await with `nats-py` library
- Python 3.11 slim Docker image
- ~128 MB memory footprint
- Basic pub/sub demonstration

### C# Basic Pub/Sub

- .NET 8 with `NATS.Client 1.1.8` library
- Alpine-based Docker image
- ~100 MB memory footprint
- Basic pub/sub demonstration

### C# Payment Monitoring

- Simulates credit card transactions with random failures
- Logs declined transactions at ERROR level for Loki
- Generic message logger for monitoring remote systems
- No console access required - monitor via NATS topics

### C# JetStream

- Modern `NATS.Net 2.4.0` library
- Message persistence to disk
- Historical message replay
- Durable consumers with at-least-once delivery
- Automatic stream creation

## Message Format

All implementations use the same JSON message schema:

```json
{
  "message_id": "nats-1-42",
  "timestamp": "2025-10-08T12:34:56.789Z",
  "source": "nats-1",
  "sequence": 42,
  "data": {
    "event_type": "user.login",
    "value": 523,
    "status": "success",
    "description": "Event description"
  }
}
```

See [`docs/message-format.md`](docs/message-format.md) for complete specification.

## Cross-Language Communication

All implementations are fully interoperable:

| Publisher | Subscriber | Status |
|-----------|------------|--------|
| Python    | Python     | ✅ Supported |
| Python    | C#         | ✅ Supported |
| C#        | Python     | ✅ Supported |
| C#        | C#         | ✅ Supported |

**Example: Python publisher → C# subscriber**

```bash
# Terminal 1: Start Python publisher
cd python
docker-compose up -d nats publisher

# Terminal 2: Start C# subscriber
cd csharp
docker-compose up -d subscriber

# View cross-language messages
docker logs -f csharp-subscriber | grep "python"
```

## Deployment Scenarios

### Scenario 1: Single VM Demo

Deploy everything on one machine for testing:

```bash
cd csharp
docker-compose up -d --build
```

See [`docs/single-vm-deployment.md`](docs/single-vm-deployment.md) for details.

### Scenario 2: Monitor Remote Payment System

Deploy PaymentPublisher on remote server (no console access) and MessageLogger locally:

```bash
# On remote server
cd csharp
docker-compose up -d payment-publisher nats

# On monitoring server
docker-compose up -d payment-monitor
```

See [`docs/payment-monitoring-setup.md`](docs/payment-monitoring-setup.md) for details.

### Scenario 3: Production with Persistence

Use JetStream versions for message durability:

```bash
cd csharp
# Uncomment jetstream-setup in docker-compose.yml
docker-compose up -d payment-publisher-js payment-monitor-js nats
```

See [`docs/upgrade-to-nats-net-2.md`](docs/upgrade-to-nats-net-2.md) for migration guide.

### Scenario 4: Catch Up After Downtime

Use MessageLogger-JetStream to replay missed messages:

```bash
cd csharp
# Set REPLAY_HISTORY=true
docker-compose up -d payment-monitor-js
# Replays ALL historical messages from JetStream stream
```

See [`docs/jetstream-historical-replay.md`](docs/jetstream-historical-replay.md) for details.

## Log Format

All implementations output structured JSON logs:

```json
{
  "timestamp": "2025-10-08T12:34:56.789Z",
  "level": "INFO",
  "logger": "nats-publisher",
  "message": "Message published",
  "message_id": "nats-1-42",
  "subject": "events.test",
  "size_bytes": 245,
  "sequence": 42,
  "event_type": "user.login"
}
```

### Querying Logs in Grafana

```logql
# All Python logs
{container_name=~".*python.*"}

# All C# logs
{container_name=~".*csharp.*"}

# Payment errors only
{container_name="payment-monitor", level="ERROR"}

# Compare publish rates
sum by (container_name) (
  rate({container_name=~".*publisher.*"}
  |= "Message published" [1m])
)

# Compare latency
avg_over_time(
  {container_name=~".*subscriber.*"}
  | json
  | unwrap latency_ms [5m]
) by (container_name)
```

## Documentation

### Getting Started
- [`README.md`](README.md) - This file (overview)
- [`python/README.md`](python/README.md) - Python implementation details
- [`csharp/README.md`](csharp/README.md) - C# implementation details

### Architecture & Design
- [`docs/architecture.md`](docs/architecture.md) - System architecture
- [`docs/component-overview.md`](docs/component-overview.md) - Component guide
- [`docs/message-format.md`](docs/message-format.md) - Message schema

### Deployment Guides
- [`docs/single-vm-deployment.md`](docs/single-vm-deployment.md) - Single VM setup
- [`docs/payment-monitoring-setup.md`](docs/payment-monitoring-setup.md) - Payment monitoring
- [`docs/jetstream-historical-replay.md`](docs/jetstream-historical-replay.md) - JetStream setup

### Advanced Topics
- [`docs/nats-subjects-guide.md`](docs/nats-subjects-guide.md) - NATS subjects/topics
- [`docs/message-logging-guide.md`](docs/message-logging-guide.md) - Message monitoring
- [`docs/upgrade-to-nats-net-2.md`](docs/upgrade-to-nats-net-2.md) - Library upgrade guide

## NATS Configuration

Shared NATS server configuration at [`nats-config/nats-server.conf`](nats-config/nats-server.conf):

- **Port 4222:** Client connections
- **Port 6222:** Cluster connections (for HA)
- **Port 8222:** HTTP monitoring endpoint
- **JetStream:** Enabled for message persistence
- **Clustering:** Optional for high availability

All implementations use this shared configuration.

## Monitoring

### NATS Server Metrics

```bash
# Server info
curl http://localhost:8222/varz | jq

# Active connections
curl http://localhost:8222/connz | jq

# Subscriptions
curl http://localhost:8222/subsz | jq

# JetStream status
curl http://localhost:8222/jsz | jq
```

### Application Metrics

All implementations log metrics periodically:
- **Publishers:** Every 50 messages
- **Subscribers:** Every 60 seconds
- **Payment systems:** Every 20 transactions

## Performance Comparison

| Metric | Python | C# Basic | C# Payment | C# JetStream |
|--------|--------|----------|------------|--------------|
| Memory | ~128 MB | ~100 MB | ~100 MB | ~120 MB |
| CPU (idle) | <0.1 core | <0.1 core | <0.1 core | <0.1 core |
| Throughput | ~1K msg/s | ~2K msg/s | ~500 tx/s | ~1.5K msg/s |
| Startup | ~2s | ~1s | ~1s | ~2s |
| Image size | ~200 MB | ~180 MB | ~180 MB | ~180 MB |

## Versions

- **NATS Server:** 2.10.7-alpine
- **Python:** 3.11 + nats-py 2.6.0
- **C# Basic/Payment:** .NET 8.0 + NATS.Client 1.1.8
- **C# JetStream:** .NET 8.0 + NATS.Net 2.4.0

## Troubleshooting

### Python Issues

See [`python/README.md`](python/README.md) for Python-specific troubleshooting.

### C# Issues

See [`csharp/README.md`](csharp/README.md) for C#-specific troubleshooting.

### Cross-Language Communication Issues

**Messages not flowing between languages:**

1. Verify NATS subject matches:
   ```bash
   docker logs nats-publisher | jq .subject | head -1
   docker logs csharp-publisher | jq .subject | head -1
   ```

2. Check both connected to same NATS server:
   ```bash
   curl http://localhost:8222/connz | jq '.connections[] | .name'
   ```

3. Verify message format compatibility:
   ```bash
   docker logs nats-publisher | jq | head -5
   docker logs csharp-publisher | jq | head -5
   ```

### Payment Monitoring Issues

See [`docs/payment-monitoring-setup.md`](docs/payment-monitoring-setup.md) troubleshooting section.

### JetStream Issues

See [`docs/jetstream-historical-replay.md`](docs/jetstream-historical-replay.md) troubleshooting section.

## Integration with Loki

This application works with the `loki-logging-stack`:

1. Deploy Loki stack on dedicated VM
2. Deploy Promtail on each NATS VM
3. Deploy Python and/or C# implementations
4. Logs automatically flow to Loki
5. View in Grafana with language/component filters

See `../loki-logging-stack/README.md` for Loki deployment.

## Contributing

When adding new language implementations:

1. Follow the message format specification ([`docs/message-format.md`](docs/message-format.md))
2. Use structured JSON logging
3. Include Docker support
4. Add integration tests
5. Document language-specific features
6. Update this README

## License

[Your License Here]

## Support

For issues:
- Check language-specific README ([`python/README.md`](python/README.md), [`csharp/README.md`](csharp/README.md))
- Review [`docs/component-overview.md`](docs/component-overview.md) for component guide
- Review [`docs/architecture.md`](docs/architecture.md) for system architecture
- Check relevant setup guides in `docs/`
