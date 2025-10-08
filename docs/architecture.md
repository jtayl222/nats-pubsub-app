# NATS PubSub Application Architecture

**Version:** 1.0
**Last Updated:** 2025-10-08

## Overview

This document describes the architecture of the NATS pub/sub application that supports multiple language implementations (Python, C#, Go, etc.) with centralized logging to Loki/Grafana.

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        NATS Cluster                              │
│                                                                   │
│  ┌──────────────┐           ┌──────────────┐                    │
│  │  NATS-1 VM   │◄─────────►│  NATS-2 VM   │                    │
│  │  (Port 6222) │  Cluster  │  (Port 6222) │                    │
│  └──────┬───────┘           └──────┬───────┘                    │
│         │                          │                             │
└─────────┼──────────────────────────┼─────────────────────────────┘
          │                          │
          │ Port 4222                │ Port 4222
          │ (Client)                 │ (Client)
          │                          │
    ┌─────┴─────┐              ┌─────┴─────┐
    │           │              │           │
┌───▼────┐  ┌──▼────────┐ ┌───▼────┐  ┌──▼────────┐
│Publish │  │Subscribe  │ │Publish │  │Subscribe  │
│Python  │  │Python     │ │C#      │  │C#         │
└───┬────┘  └──┬────────┘ └───┬────┘  └──┬────────┘
    │          │              │          │
    └──────────┴──────────────┴──────────┘
               │
          JSON Logs
               │
               ▼
         ┌──────────┐
         │ Promtail │
         └────┬─────┘
              │
              ▼
         ┌─────────┐
         │  Loki   │
         └────┬────┘
              │
              ▼
         ┌─────────┐
         │ Grafana │
         └─────────┘
```

## Components

### 1. NATS Server Cluster

**Purpose:** Distributed message broker providing pub/sub messaging.

**Configuration:**
- Port 4222: Client connections
- Port 6222: Cluster connections (inter-node communication)
- Port 8222: HTTP monitoring/metrics

**Features:**
- Automatic reconnection
- Message persistence via JetStream
- High availability through clustering
- Subject-based routing

**Deployment:**
- 2+ instances for HA
- Clustered across multiple VMs
- Shared configuration via `nats-config/nats-server.conf`

### 2. Publishers

**Purpose:** Generate and publish messages to NATS subjects.

**Available Implementations:**
- Python (async with `nats-py`)
- C# (.NET 8 with `NATS.Client`)
- Go (future)

**Behavior:**
- Publishes messages at configurable intervals (default: 2 seconds)
- Generates structured messages following the schema
- Tracks metrics (message count, error rate, uptime)
- Automatic reconnection on connection loss
- JSON structured logging to stdout

**Configuration:**
- `NATS_URL`: NATS server connection URL
- `NATS_SUBJECT`: Subject to publish to (default: `events.test`)
- `PUBLISH_INTERVAL`: Seconds between messages
- `HOSTNAME`: Unique identifier

### 3. Subscribers

**Purpose:** Subscribe to NATS subjects and process messages.

**Available Implementations:**
- Python (async with `nats-py`)
- C# (.NET 8 with `NATS.Client`)
- Go (future)

**Behavior:**
- Subscribes to configured subject patterns
- Calculates message latency
- Tracks metrics (message count, latency, error rate)
- Optional queue group for load balancing
- JSON structured logging to stdout

**Configuration:**
- `NATS_URL`: NATS server connection URL
- `NATS_SUBJECT`: Subject pattern to subscribe to
- `QUEUE_GROUP`: Queue group name (for load balancing)
- `HOSTNAME`: Unique identifier

### 4. Logging Pipeline

**Purpose:** Collect, aggregate, and visualize application logs.

**Components:**

1. **Applications** → JSON logs to stdout
2. **Docker** → Captures container stdout/stderr
3. **Promtail** → Discovers containers, reads logs via Docker socket
4. **Loki** → Ingests, stores, and indexes logs
5. **Grafana** → Queries and visualizes logs

**Log Format:** Structured JSON (see `message-format.md`)

## Message Flow

### Publishing Flow

```
1. Publisher generates message
   ↓
2. Serializes to JSON following schema
   ↓
3. Publishes to NATS subject
   ↓
4. NATS distributes to subscribers
   ↓
5. Publisher logs "Message published" (JSON)
   ↓
6. Docker captures log
   ↓
7. Promtail reads and labels log
   ↓
8. Loki stores log
   ↓
9. Grafana displays log
```

### Subscription Flow

```
1. Subscriber receives message from NATS
   ↓
2. Deserializes JSON
   ↓
3. Validates against schema
   ↓
4. Calculates latency
   ↓
5. Processes message
   ↓
6. Logs "Message received" with metadata (JSON)
   ↓
7. Docker captures log
   ↓
8. Promtail reads and labels log
   ↓
9. Loki stores log
   ↓
10. Grafana displays log
```

## Cross-Language Compatibility

### Design Principles

1. **Language-agnostic message format** (JSON)
2. **Standardized logging format** (structured JSON)
3. **Shared NATS configuration**
4. **Independent deployment** (each language in own container)
5. **Consistent metrics** (all track same KPIs)

### Interoperability Matrix

| Publisher | Subscriber | Compatible? |
|-----------|------------|-------------|
| Python    | Python     | ✅ Yes      |
| Python    | C#         | ✅ Yes      |
| C#        | Python     | ✅ Yes      |
| C#        | C#         | ✅ Yes      |
| Go        | Any        | ✅ Yes      |

All implementations publish/subscribe to the same NATS subjects using the same message schema.

## Deployment Models

### Model 1: Single Language Per VM

```
NATS-1 VM:
  - NATS Server
  - Python Publisher
  - Python Subscriber

NATS-2 VM:
  - NATS Server
  - C# Publisher
  - C# Subscriber
```

**Pros:** Clear separation, easier troubleshooting
**Cons:** Less cross-language testing

### Model 2: Mixed Languages Per VM

```
NATS-1 VM:
  - NATS Server
  - Python Publisher
  - C# Subscriber

NATS-2 VM:
  - NATS Server
  - C# Publisher
  - Python Subscriber
```

**Pros:** Validates cross-language compatibility
**Cons:** More complex deployment

### Model 3: Multiple Publishers/Subscribers

```
NATS-1 VM:
  - NATS Server
  - Python Publisher (x2)
  - C# Publisher (x2)
  - Python Subscriber (x2, queue group)
  - C# Subscriber (x2, queue group)
```

**Pros:** High throughput, load balancing
**Cons:** Higher resource usage

## Scaling Strategies

### Horizontal Scaling

**Publishers:**
```bash
# Scale to 3 Python publishers
docker-compose up -d --scale publisher=3
```

**Subscribers with Queue Groups:**
```bash
# Scale to 5 subscribers (load balanced)
docker-compose up -d --scale subscriber=5
```

All subscribers in the same queue group share message delivery.

### Vertical Scaling

Increase resources in `docker-compose.yml`:

```yaml
deploy:
  resources:
    limits:
      cpus: '2'
      memory: 1G
```

### NATS Cluster Scaling

Add more NATS nodes:

1. Deploy new VM with NATS server
2. Update `routes` in `nats-server.conf`
3. Restart all NATS instances

## Monitoring & Observability

### Metrics

**Publisher Metrics:**
- Total messages published
- Messages per second
- Error count and rate
- Uptime

**Subscriber Metrics:**
- Total messages received
- Messages per second
- Average latency
- Error count and rate
- Uptime

**NATS Server Metrics:**
- Number of connections
- Number of subscriptions
- Messages in/out per second
- Bytes in/out
- Number of routes (cluster)

### Logging

**Log Levels:**
- `DEBUG`: Detailed diagnostic info
- `INFO`: Normal operational messages
- `WARN`: Warning conditions
- `ERROR`: Error events

**Log Fields:**
- `timestamp`: ISO 8601 with timezone
- `level`: Log level
- `logger`: Component name
- `message`: Human-readable message
- `message_id`: For message-specific logs
- `latency_ms`: For subscriber logs
- Additional metadata

### Querying Logs

**Grafana LogQL Examples:**

```logql
# All logs from Python publisher
{container_name="nats-publisher"}

# Error logs from any component
{container_name=~"nats-.*"} | json | level="ERROR"

# Messages with high latency (>10ms)
{container_name="nats-subscriber"} | json | latency_ms > 10

# Publish rate
rate({container_name="nats-publisher"} |= "Message published" [1m])

# Compare Python vs C# latency
avg_over_time(
  {container_name=~"nats-subscriber.*"}
  | json
  | unwrap latency_ms [5m]
) by (container_name)
```

## Fault Tolerance

### Connection Loss Handling

**Publishers:**
1. Detect connection loss
2. Log warning
3. Attempt reconnection (exponential backoff)
4. Buffer messages (up to memory limit)
5. Resume publishing when reconnected

**Subscribers:**
1. Detect connection loss
2. Log warning
3. Attempt reconnection
4. Resume subscription when reconnected
5. NATS re-delivers missed messages (if JetStream enabled)

### NATS Server Failure

- Clients automatically reconnect to other cluster members
- No message loss if JetStream persistence enabled
- Cluster continues operating with remaining nodes

### Message Ordering

- **Within single publisher:** Guaranteed order on same subject
- **Multiple publishers:** No global ordering guarantee
- **Solution:** Use `sequence` field in message for ordering

## Performance Characteristics

### Typical Throughput

**Per Publisher:**
- Default: 0.5 messages/second (2-second interval)
- Configurable up to 1000+ messages/second

**Per Subscriber:**
- Handles 1000+ messages/second
- Latency typically <5ms (local network)

**NATS Server:**
- Millions of messages/second (depends on hardware)
- Sub-millisecond latency

### Resource Usage

**Python Implementation:**
- CPU: <0.5 core per publisher/subscriber
- Memory: ~128 MB per container

**C# Implementation:**
- CPU: <0.5 core per publisher/subscriber
- Memory: ~100 MB per container

**NATS Server:**
- CPU: 1-2 cores
- Memory: 256-512 MB

## Security Considerations

### Authentication

Configure in `nats-server.conf`:

```conf
authorization {
  user: app_user
  password: $APP_PASSWORD
}
```

### TLS Encryption

```conf
tls {
  cert_file: "/etc/nats/server-cert.pem"
  key_file: "/etc/nats/server-key.pem"
  ca_file: "/etc/nats/ca.pem"
  verify: true
}
```

### Network Isolation

- Deploy NATS on private network
- Use firewall rules to restrict access
- Only expose monitoring endpoint (8222) if needed

## Testing Strategy

### Unit Tests

- Message serialization/deserialization
- Schema validation
- Error handling

### Integration Tests

- End-to-end message flow
- Cross-language communication
- Connection failure recovery
- Log format validation

### Performance Tests

- Throughput benchmarks
- Latency measurements
- Resource usage profiling

## Future Enhancements

- Go implementation
- Message persistence with JetStream
- Request-reply patterns
- Stream processing with NATS Streaming
- Authentication and authorization
- TLS encryption
- Prometheus metrics export
- Custom Grafana dashboards per language

## References

- [NATS Documentation](https://docs.nats.io/)
- [Message Format Specification](message-format.md)
- [Deployment Guide](deployment-guide.md)
