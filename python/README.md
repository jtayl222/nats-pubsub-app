# NATS PubSub - Python Implementation

Python 3.11+ implementation of NATS publisher/subscriber with structured JSON logging.

## Scope

This Python implementation provides:
- ✅ Basic publisher/subscriber demonstration
- ✅ Structured JSON logging for Loki integration
- ✅ Cross-language message compatibility (works with C#, Go implementations)
- ✅ Async NATS client with `nats-py`
- ✅ Docker containerization

This implementation does **NOT** include:
- ❌ Payment simulation (see `csharp/PaymentPublisher/`)
- ❌ NATS message monitoring/logging service (see `csharp/MessageLogger/`)
- ❌ JetStream support with historical replay (see `csharp/*-JetStream/`)

For production payment monitoring, message capture, and JetStream features, use the C# implementation.

## Features

- **Publisher**: Async message generation using `nats-py`
- **Subscriber**: Async message consumption with latency tracking
- **Structured Logging**: JSON format compatible with Loki/Promtail
- **Docker Support**: Multi-stage builds with Alpine runtime
- **Cross-Language**: Compatible with C#, Go, and other implementations

## Quick Start

### Prerequisites

- Docker 20.10+
- Docker Compose v2.0+

### Deploy

```bash
cd python

# Copy environment template
cp .env.example .env

# Edit if needed
nano .env

# Build and start
docker-compose up -d --build

# Check status
docker-compose ps
```

### View Logs

```bash
# All services
docker-compose logs -f

# Publisher only
docker-compose logs -f publisher

# Subscriber only
docker-compose logs -f subscriber
```

## Configuration

### Environment Variables

Edit `.env` file:

```bash
# NATS connection
NATS_URL=nats://nats:4222

# Message subject
NATS_SUBJECT=events.test

# Publisher: seconds between messages
PUBLISH_INTERVAL=2.0

# Subscriber: queue group for load balancing (optional)
QUEUE_GROUP=workers
```

### Docker Compose

```yaml
publisher:
  environment:
    - NATS_URL=nats://nats:4222
    - NATS_SUBJECT=events.test
    - HOSTNAME=python-publisher
    - PUBLISH_INTERVAL=2.0

subscriber:
  environment:
    - NATS_URL=nats://nats:4222
    - NATS_SUBJECT=events.test
    - HOSTNAME=python-subscriber
    - QUEUE_GROUP=workers  # Optional: for load balancing
```

## Message Format

Messages follow the shared schema defined in [`../docs/message-format.md`](../docs/message-format.md).

### Published Message

```json
{
  "message_id": "python-publisher-42",
  "timestamp": "2025-10-08T15:30:45.123456Z",
  "source": "python-publisher",
  "sequence": 42,
  "data": {
    "event_type": "user.login",
    "value": 73,
    "status": "success",
    "description": "User login event #42"
  }
}
```

### Log Format

```json
{
  "timestamp": "2025-10-08T15:30:45.123456Z",
  "level": "INFO",
  "logger": "nats-publisher",
  "message": "Message published",
  "module": "publisher",
  "function": "publish_loop",
  "message_id": "python-publisher-42",
  "subject": "events.test",
  "sequence": 42
}
```

## Cross-Language Compatibility

This implementation is fully compatible with other language implementations in this monorepo:

### Python ↔ C# Communication

```bash
# Start Python publisher
cd python && docker-compose up -d publisher

# Start C# subscriber (from ../csharp)
cd ../csharp && docker-compose up -d subscriber

# C# subscriber will receive Python messages
docker logs -f csharp-subscriber | grep "python"
```

### C# → Python Communication

```bash
# Start C# publisher
cd csharp && docker-compose up -d publisher

# Start Python subscriber (from ../python)
cd ../python && docker-compose up -d subscriber

# Python subscriber will receive C# messages
docker logs -f python-subscriber | grep "csharp"
```

## Architecture

See [`../docs/architecture.md`](../docs/architecture.md) for complete system architecture.

```
┌──────────────┐         ┌──────────────┐
│   Publisher  │────────►│ NATS Server  │
│   (Python)   │         │              │
└──────────────┘         └──────┬───────┘
                                │
                         ┌──────▼───────┐
                         │  Subscriber  │
                         │  (Python/C#) │
                         └──────────────┘
```

## Development

### Local Development (without Docker)

```bash
# Install dependencies
cd publisher
pip install -r requirements.txt

# Run publisher
export NATS_URL=nats://localhost:4222
export NATS_SUBJECT=events.test
python publisher.py

# In another terminal, run subscriber
cd subscriber
pip install -r requirements.txt
export NATS_URL=nats://localhost:4222
export NATS_SUBJECT=events.test
python subscriber.py
```

### Testing

```bash
# Run integration tests
./tests/integration_test.sh

# Or manually test message flow
docker-compose up -d

# Wait a few seconds
sleep 10

# Check publisher sent messages
docker logs python-publisher | grep "Message published" | wc -l

# Check subscriber received messages
docker logs python-subscriber | grep "Message received" | wc -l
```

## Troubleshooting

### Publisher Not Connecting

```bash
# Check NATS is running
docker logs nats-server

# Check publisher logs
docker logs python-publisher

# Verify NATS is accessible
docker exec python-publisher nc -zv nats 4222
```

### Subscriber Not Receiving Messages

```bash
# Verify subjects match
docker logs python-publisher | head -1 | jq .subject
docker logs python-subscriber | head -1 | jq .subject

# Check subscriber is running
docker logs python-subscriber

# Verify queue group (if used)
docker exec python-subscriber env | grep QUEUE_GROUP
```

### Logs Not Appearing in Loki

```bash
# Check Promtail is running
systemctl status promtail  # On host VM

# Verify container labels
docker inspect python-publisher | grep -A5 Labels

# Check log format is JSON
docker logs python-publisher | head -1 | jq
```

## Performance

- **Publisher**: Configurable rate (default: 0.5 msg/s)
- **Subscriber**: Handles 1000+ msg/s
- **Latency**: ~1-2ms (local Docker network)
- **Memory**: ~50MB per container

## Advanced Usage

### Scaling

```bash
# Scale publishers
docker-compose up -d --scale publisher=3

# Scale subscribers (with queue group for load balancing)
docker-compose up -d --scale subscriber=5
```

### Custom Event Types

Edit `publisher/publisher.py`:

```python
event_types = [
    "user.login",
    "user.logout",
    "order.created",
    "payment.processed"
]
```

### Integration with C# Payment System

To monitor C# payment transactions from Python:

```bash
# Start C# payment publisher
cd ../csharp && docker-compose up -d payment-publisher

# Start Python subscriber on payment topics
cd ../python
# Edit docker-compose.yml subscriber environment:
# - NATS_SUBJECT=payments.>

docker-compose up -d subscriber

# View payment messages
docker logs -f python-subscriber | grep "payments"
```

## Related Documentation

- [Message Format Specification](../docs/message-format.md)
- [System Architecture](../docs/architecture.md)
- [C# Implementation](../csharp/README.md)
- [Payment Monitoring Setup](../docs/payment-monitoring-setup.md) - C# only
- [JetStream Historical Replay](../docs/jetstream-historical-replay.md) - C# only

## Support

For issues or questions:
- Check [troubleshooting section](#troubleshooting)
- Review [architecture documentation](../docs/architecture.md)
- See C# implementation for advanced features
