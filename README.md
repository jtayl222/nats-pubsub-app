# NATS PubSub Application - Multi-Language Monorepo

Production-ready NATS publisher/subscriber application with multiple language implementations and structured JSON logging for Loki integration.

## Overview

This monorepo contains NATS pub/sub implementations in multiple languages:
- **Python** - Async implementation with `nats-py`
- **C#** - .NET 8 implementation with `NATS.Client`
- **Go** - (Future)

All implementations:
- Follow the same message schema (see [`docs/message-format.md`](docs/message-format.md))
- Use structured JSON logging for Loki/Promtail
- Are fully interoperable (Python publisher → C# subscriber, etc.)
- Share the same NATS server configuration

## Repository Structure

```
nats-pubsub-app/
├── README.md                           # This file
├── .gitignore
├── nats-config/                        # Shared NATS configuration
│   └── nats-server.conf
├── docs/                               # Shared documentation
│   ├── architecture.md                 # System architecture
│   ├── message-format.md               # Message schema specification
│   └── deployment-guide.md             # Deployment instructions
├── python/                             # Python implementation
│   ├── README.md
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
│   ├── scripts/
│   │   ├── deploy.sh
│   │   └── validate.sh
│   └── tests/
│       └── integration_test.sh
├── csharp/                             # C# implementation
│   ├── README.md
│   ├── docker-compose.yml
│   ├── .env.example
│   ├── Publisher/
│   │   ├── Dockerfile
│   │   ├── Publisher.csproj
│   │   └── Program.cs
│   ├── Subscriber/
│   │   ├── Dockerfile
│   │   ├── Subscriber.csproj
│   │   └── Program.cs
│   ├── scripts/
│   │   ├── deploy.sh
│   │   └── validate.sh
│   └── tests/
│       └── integration_test.sh
└── scripts/                            # Shared deployment scripts
    ├── deploy-python.sh                # Deploy Python stack
    ├── deploy-csharp.sh                # Deploy C# stack
    ├── deploy-all.sh                   # Deploy both stacks
    └── cleanup.sh                      # Clean up all resources
```

## Quick Start

### Option 1: Deploy Python Implementation

```bash
# Deploy Python publisher/subscriber
cd python
./scripts/deploy.sh
./scripts/validate.sh
```

### Option 2: Deploy C# Implementation

```bash
# Deploy C# publisher/subscriber
cd csharp
./scripts/deploy.sh
./scripts/validate.sh
```

### Option 3: Deploy Both (Cross-Language Test)

```bash
# Deploy both Python and C# implementations
./scripts/deploy-all.sh
```

This creates a mixed environment where Python and C# publishers/subscribers can communicate with each other.

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                    NATS Server Cluster                    │
│                    (Shared by all langs)                  │
└─────────────┬────────────────────────────┬───────────────┘
              │                            │
       ┌──────┴──────┐             ┌──────┴──────┐
       │   Python    │             │     C#      │
       │             │             │             │
   ┌───▼────┐   ┌───▼────┐   ┌───▼────┐   ┌───▼────┐
   │Publish │   │Subscribe│  │Publish │   │Subscribe│
   │  .py   │   │  .py    │  │  .cs   │   │  .cs    │
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

### C# Implementation

- .NET 8 with `NATS.Client` library
- Alpine-based Docker image
- ~100 MB memory footprint
- Ahead-of-time (AOT) compilation ready

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
    "random_field": "alpha"
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

Test cross-language communication:

```bash
# Start Python publisher and C# subscriber
cd python && docker-compose up -d nats publisher
cd ../csharp && docker-compose up -d subscriber

# Or use the combined deployment
./scripts/deploy-all.sh
```

## Deployment Scenarios

### Scenario 1: Single Language Per VM

Deploy Python on nats-1, C# on nats-2:

```bash
# On nats-1
cd python
echo "HOSTNAME=nats-1" > .env
./scripts/deploy.sh

# On nats-2
cd csharp
echo "HOSTNAME=nats-2" > .env
./scripts/deploy.sh
```

### Scenario 2: Mixed Languages Per VM

Deploy both on each VM for cross-language testing:

```bash
# On nats-1
./scripts/deploy-all.sh

# On nats-2
./scripts/deploy-all.sh
```

### Scenario 3: Language-Specific Scaling

Scale specific implementations:

```bash
# Scale Python publishers
cd python
docker-compose up -d --scale publisher=3

# Scale C# subscribers
cd csharp
docker-compose up -d --scale subscriber=5
```

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

### Querying Cross-Language Logs in Grafana

```logql
# All Python logs
{container_name=~".*python.*"}

# All C# logs
{container_name=~".*csharp.*"}

# Compare Python vs C# publish rates
sum by (container_name) (
  rate({container_name=~"nats-(publisher|csharp-publisher).*"}
  |= "Message published" [1m])
)

# Compare Python vs C# latency
avg_over_time(
  {container_name=~"nats-(subscriber|csharp-subscriber).*"}
  | json
  | unwrap latency_ms [5m]
) by (container_name)
```

## Documentation

### Shared Documentation

- [`docs/architecture.md`](docs/architecture.md) - System architecture and design
- [`docs/message-format.md`](docs/message-format.md) - Message schema specification
- [`docs/deployment-guide.md`](docs/deployment-guide.md) - Deployment instructions

### Language-Specific Documentation

- [`python/README.md`](python/README.md) - Python implementation details
- [`csharp/README.md`](csharp/README.md) - C# implementation details

## NATS Configuration

Shared NATS server configuration at [`nats-config/nats-server.conf`](nats-config/nats-server.conf):

- Port 4222: Client connections
- Port 6222: Cluster connections
- Port 8222: HTTP monitoring
- JetStream enabled for persistence
- Clustering support for HA

All implementations use this shared configuration.

## Testing

### Integration Tests

**Python:**
```bash
cd python
./tests/integration_test.sh
# 13 tests should pass
```

**C#:**
```bash
cd csharp
./tests/integration_test.sh
# 13 tests should pass
```

### Cross-Language Test

```bash
# Deploy both implementations
./scripts/deploy-all.sh

# Verify Python publisher → C# subscriber
docker logs csharp-subscriber | grep "Message received" | grep "python"

# Verify C# publisher → Python subscriber
docker logs nats-subscriber | grep "Message received" | grep "csharp"
```

## Monitoring

### NATS Server Metrics

```bash
# Server info (works for all implementations)
curl http://localhost:8222/varz | jq

# Connections (shows Python, C#, etc.)
curl http://localhost:8222/connz | jq

# Subscriptions by language
curl http://localhost:8222/subsz | jq
```

### Application Metrics

Both implementations log metrics periodically:
- Publishers: Every 50 messages
- Subscribers: Every 60 seconds

## Performance Comparison

| Metric | Python | C# |
|--------|--------|-----|
| Memory | ~128 MB | ~100 MB |
| CPU (idle) | <0.1 core | <0.1 core |
| CPU (1000 msg/s) | ~0.3 core | ~0.2 core |
| Startup time | ~2s | ~1s |
| Image size | ~200 MB | ~180 MB |

## Troubleshooting

### Python Issues

See [`python/README.md`](python/README.md) for Python-specific troubleshooting.

### C# Issues

See [`csharp/README.md`](csharp/README.md) for C#-specific troubleshooting.

### Cross-Language Issues

**Messages not flowing between languages:**

1. Verify NATS subject matches:
   ```bash
   # Check Python
   docker logs nats-publisher | head -5 | jq .subject

   # Check C#
   docker logs csharp-publisher | head -5 | jq .subject
   ```

2. Check message schema:
   ```bash
   # Python message
   docker logs nats-publisher | head -5 | jq

   # C# message
   docker logs csharp-publisher | head -5 | jq
   ```

3. Verify both connected to same NATS:
   ```bash
   curl http://localhost:8222/connz | jq '.connections[] | .name'
   ```

## Adding New Languages

To add a new language implementation (e.g., Go):

1. Create `go/` directory with same structure as `python/` or `csharp/`
2. Implement publisher/subscriber following `docs/message-format.md`
3. Use structured JSON logging
4. Create language-specific `docker-compose.yml`
5. Add deployment script in `go/scripts/`
6. Create integration tests
7. Update this README with new implementation

## Integration with Loki

This application works with the `loki-logging-stack`:

1. Deploy Loki stack (on dedicated VM)
2. Deploy Promtail on each NATS VM
3. Deploy Python and/or C# implementations
4. Logs automatically flow to Loki
5. View in Grafana with language filters

See `../loki-logging-stack/README.md` for Loki deployment.

## Scripts Reference

### Root Level Scripts

- `scripts/deploy-all.sh` - Deploy both Python and C#
- `scripts/deploy-python.sh` - Deploy Python only
- `scripts/deploy-csharp.sh` - Deploy C# only
- `scripts/cleanup.sh` - Stop and remove all containers

### Language-Specific Scripts

- `python/scripts/deploy.sh` - Deploy Python stack
- `python/scripts/validate.sh` - Validate Python deployment
- `csharp/scripts/deploy.sh` - Deploy C# stack
- `csharp/scripts/validate.sh` - Validate C# deployment

## Versions

- **NATS Server**: 2.10.7
- **Python**: 3.11 + nats-py 2.6.0
- **C#**: .NET 8.0 + NATS.Client 2.0.0

## Contributing

When adding new language implementations:

1. Follow the message format specification
2. Use structured JSON logging
3. Include Docker support
4. Add integration tests
5. Document language-specific features
6. Update this README

## License

[Your License Here]

## Support

For issues:
- Check language-specific README
- Review [`docs/architecture.md`](docs/architecture.md)
- Run validation scripts
- Run integration tests
- Open issue in GitLab
