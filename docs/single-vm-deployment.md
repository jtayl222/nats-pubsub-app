# Single VM Deployment Guide

Run the entire NATS + Loki logging stack on one Linux machine (Rocky Linux, Ubuntu, etc.)

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Single Rocky Linux VM                   │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │   Publisher  │  │  Subscriber  │  │     NATS     │      │
│  │  (Container) │──│  (Container) │──│  (Container) │      │
│  └──────────────┘  └──────────────┘  └──────────────┘      │
│         │                 │                                  │
│         └─────────┬───────┘                                  │
│                   │ (logs)                                   │
│         ┌─────────▼────────┐                                │
│         │     Promtail     │                                │
│         │   (Container)    │                                │
│         └─────────┬────────┘                                │
│                   │                                          │
│         ┌─────────▼────────┐      ┌──────────────┐         │
│         │       Loki       │      │   Grafana    │         │
│         │   (Container)    │◄─────│  (Container) │         │
│         └──────────────────┘      └──────────────┘         │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

- Rocky Linux VM (or Ubuntu/Debian)
- Docker 20.10+
- Docker Compose v2.0+
- 2 CPU cores minimum
- 4GB RAM minimum
- 20GB disk space

## Quick Start

### 1. Install Docker

```bash
# Rocky Linux 9
sudo dnf install -y docker docker-compose-plugin
sudo systemctl enable --now docker
sudo usermod -aG docker $USER
newgrp docker
```

### 2. Clone Repositories

```bash
cd ~
git clone <loki-logging-stack-repo-url> loki-logging-stack
git clone <nats-pubsub-app-repo-url> nats-pubsub-app
```

### 3. Deploy Loki Stack

```bash
cd ~/loki-logging-stack
docker compose up -d

# Wait for services to start
sleep 10

# Verify
docker compose ps
curl http://localhost:3100/ready  # Should return "ready"
curl http://localhost:3000/api/health  # Should return OK
```

### 4. Deploy Promtail (as Container)

Instead of installing Promtail as a systemd service, run it as a container:

```bash
cd ~/loki-logging-stack

# Create Promtail config for localhost
cat > promtail-local.yaml <<'EOF'
server:
  http_listen_port: 9080
  grpc_listen_port: 0
  log_level: info

positions:
  filename: /tmp/positions.yaml

clients:
  - url: http://loki:3100/loki/api/v1/push

scrape_configs:
  - job_name: docker
    docker_sd_configs:
      - host: unix:///var/run/docker.sock
        refresh_interval: 5s

    relabel_configs:
      - source_labels: ['__meta_docker_container_name']
        regex: '/(.*)'
        target_label: 'container_name'

      - source_labels: ['__meta_docker_container_id']
        target_label: 'container_id'

      - source_labels: ['__meta_docker_container_image']
        target_label: 'image'

      - replacement: 'localhost'
        target_label: 'host'

      - source_labels: ['__meta_docker_container_label_language']
        target_label: 'language'

      - source_labels: ['__meta_docker_container_label_component']
        target_label: 'component'

    pipeline_stages:
      - json:
          expressions:
            level: level
            message: message
            timestamp: timestamp

      - regex:
          expression: '(?P<level>DEBUG|INFO|WARN|ERROR|FATAL)'

      - timestamp:
          source: timestamp
          format: RFC3339Nano

      - labels:
          level:
EOF

# Add Promtail to docker-compose.yml
cat >> docker-compose.yml <<'EOF'

  promtail:
    image: grafana/promtail:2.9.3
    container_name: promtail
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ./promtail-local.yaml:/etc/promtail/config.yaml:ro
    command: -config.file=/etc/promtail/config.yaml
    networks:
      - loki
    restart: unless-stopped
EOF

# Restart stack with Promtail
docker compose up -d
```

### 5. Deploy NATS Application Stack

**Option A: Using C# Implementation**

```bash
cd ~/nats-pubsub-app/csharp

# Use the standalone NATS config
cp ../nats-config/nats-server-standalone.conf ../nats-config/nats-server-local.conf

# Update docker-compose to use standalone config
# Edit docker-compose.yml to change the config volume mount

# Build and start
docker compose build
docker compose up -d

# Verify
docker compose ps
curl http://localhost:8222/varz  # NATS monitoring
```

**Option B: Using Python Implementation**

```bash
cd ~/nats-pubsub-app/python

# Same process - use standalone config
docker compose build
docker compose up -d
```

**Option C: Run Both C# and Python Together**

To demonstrate cross-language compatibility:

```bash
# Start C# publisher + Python subscriber
cd ~/nats-pubsub-app/csharp
docker compose up -d nats publisher

cd ~/nats-pubsub-app/python
docker compose up -d subscriber

# Or vice versa - Python publisher + C# subscriber
```

### 6. Verify Log Ingestion

```bash
# Check Loki has logs
curl -s "http://localhost:3100/loki/api/v1/label/container_name/values" | jq

# Should show:
# [
#   "csharp-publisher",
#   "csharp-subscriber",
#   "nats-server"
# ]
```

### 7. Access Grafana

1. Open browser: http://localhost:3000
2. Login: `admin` / `admin` (change password when prompted)
3. Go to **Explore** (compass icon)
4. Select **Loki** datasource
5. Run queries:

```logql
{job="docker"}
{container_name="csharp-publisher"}
{language="csharp"} |= "Published message"
{container_name="csharp-subscriber"} | json | level="INFO"
```

## Understanding NATS in This Setup

### What is NATS?

NATS is **just a message broker** - similar to:
- Kafka (but simpler and faster)
- RabbitMQ (but lighter weight)
- Redis Pub/Sub (but with more features)

### NATS Has NO Relation to Proxmox

NATS is standalone software that can run:
- In Docker containers ✓ (what you're doing)
- On bare metal Linux
- In Kubernetes
- On Windows/Mac
- In any cloud (AWS, Azure, GCP)

**Proxmox was just your hypervisor choice** - you could have used:
- VMware ✓ (what you're using now)
- VirtualBox
- Hyper-V
- KVM/QEMU
- Cloud VMs
- No VMs at all (bare metal)

### NATS Architecture

```
┌─────────────┐         ┌──────────────┐         ┌─────────────┐
│  Publisher  │────────►│ NATS Server  │────────►│ Subscriber  │
│             │         │              │         │             │
│ Publishes   │         │ Routes       │         │ Receives    │
│ to subject  │         │ messages     │         │ from        │
│ "events"    │         │              │         │ "events"    │
└─────────────┘         └──────────────┘         └─────────────┘
```

**In your setup:**
- NATS Server: Docker container listening on port 4222
- Publisher: C# app connecting to `nats://nats:4222`
- Subscriber: C# app connecting to `nats://nats:4222`

### Clustering (Optional)

In your Proxmox setup, you had 2 NATS servers clustered together:

```
VM1: NATS Server 1 ──┐
                     ├──► Clustered for HA
VM2: NATS Server 2 ──┘
```

**For single VM, you don't need clustering** - just run one NATS server.

### JetStream (Built into NATS)

JetStream provides:
- Message persistence (survives restarts)
- Message replay (like Kafka)
- At-least-once delivery
- Consumer groups

It's **optional** - your current setup uses basic pub/sub (fire-and-forget).

## Port Reference

| Service | Port | Purpose |
|---------|------|---------|
| NATS | 4222 | Client connections |
| NATS | 8222 | HTTP monitoring/metrics |
| NATS | 6222 | Cluster connections (not needed for single VM) |
| Loki | 3100 | Log ingestion + query API |
| Grafana | 3000 | Web UI |
| Promtail | 9080 | Metrics endpoint |

## Networking

All containers run on the same Docker network:

```bash
# Check network
docker network ls
docker network inspect loki-logging-stack_loki

# All containers can reach each other by name:
# - nats (from publisher/subscriber)
# - loki (from promtail/grafana)
# - grafana (from your browser via localhost:3000)
```

## Comparison: Multi-VM vs Single-VM

### Multi-VM (Proxmox Setup)

```
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│   VM 1      │  │   VM 2      │  │  Loki VM    │
│             │  │             │  │             │
│ NATS Node 1 │  │ NATS Node 2 │  │    Loki     │
│ Publisher   │  │ Subscriber  │  │   Grafana   │
│ Promtail────┼──┼─────────────┼──┼►           │
└─────────────┘  └─────────────┘  └─────────────┘
```

**Pros:** High availability, distributed, realistic production setup
**Cons:** More complex, requires multiple VMs

### Single-VM (Current Setup)

```
┌────────────────────────────────┐
│        Single VM               │
│                                │
│  NATS + Publisher + Subscriber │
│  Loki + Grafana + Promtail     │
└────────────────────────────────┘
```

**Pros:** Simple, fast to deploy, low resource usage
**Cons:** Single point of failure, not production-ready

## Troubleshooting

### Check all containers are running

```bash
docker ps -a
```

### View logs

```bash
# NATS
docker logs -f <nats-container-name>

# Publisher
docker logs -f <publisher-container-name>

# Subscriber
docker logs -f <subscriber-container-name>

# Loki
docker logs -f loki

# Grafana
docker logs -f grafana
```

### Test NATS connectivity

```bash
# From inside publisher container
docker exec -it <publisher-container> sh
nc -zv nats 4222
```

### Reset everything

```bash
# Stop all
cd ~/loki-logging-stack && docker compose down
cd ~/nats-pubsub-app/csharp && docker compose down

# Remove volumes (WARNING: deletes all data)
docker volume prune

# Start fresh
cd ~/loki-logging-stack && docker compose up -d
cd ~/nats-pubsub-app/csharp && docker compose up -d
```

## Next Steps

1. **Add more publishers/subscribers**: Scale with `docker compose up -d --scale publisher=3`
2. **Enable JetStream**: Use durable consumers for guaranteed delivery
3. **Add authentication**: Uncomment auth section in nats-server.conf
4. **Test failover**: Stop NATS, watch apps reconnect automatically
5. **Create Grafana alerts**: Set up alerts for error log patterns
6. **Export metrics**: Add Prometheus for NATS metrics visualization

## Summary

- NATS is just messaging software (like Kafka)
- Runs anywhere Docker runs
- No dependency on Proxmox, VMware, or any hypervisor
- Single VM deployment is perfect for POC/testing
- Multi-VM clustering only needed for HA in production
