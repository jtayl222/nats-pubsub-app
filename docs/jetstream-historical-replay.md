# JetStream Historical Message Replay

How to capture and replay historical NATS messages using JetStream.

## Current Setup (No History)

### Basic Pub/Sub (What You Have Now)

```
Publisher                    MessageLogger
    │                              │
    │ Publish("payments.declined") │
    ├──────────────────────────────┤
    │                              │ ✅ Received
    │                              │
    │ Publish("payments.declined") │
    ├──────────────X               │ ❌ Monitor was down
    │                              │
    │                              │ Monitor starts
    │                              │ Subscribe()
    │ Publish("payments.declined") │
    ├──────────────────────────────┤
    │                              │ ✅ Received
```

**Problems:**
- ❌ Messages published before subscription are **lost forever**
- ❌ If monitor crashes, messages during downtime are **lost**
- ❌ No replay capability
- ❌ No message history

## JetStream Solution (With History)

### Architecture

```
┌──────────────────┐           ┌──────────────────┐
│   Publisher      │           │   JetStream      │
│                  │──publish──►│   Stream         │
│ (JetStream pub)  │           │   (persists)     │
└──────────────────┘           └────────┬─────────┘
                                        │
                                        │ stores messages
                                        │
                                        ▼
                               ┌─────────────────┐
                               │  Disk Storage   │
                               │  - Retention    │
                               │  - Replay       │
                               └────────┬────────┘
                                        │
                                        │ replay from any point
                                        ▼
                               ┌─────────────────┐
                               │  MessageLogger  │
                               │  (JS Consumer)  │
                               │  - Durable      │
                               │  - Resumable    │
                               └─────────────────┘
```

## Implementation

### Step 1: Create JetStream Stream

The stream must be created **before** publishing messages.

**Create stream configuration:**

```bash
# On NATS server or via nats CLI
nats stream add PAYMENTS \
  --subjects "payments.>" \
  --storage file \
  --retention limits \
  --max-msgs=-1 \
  --max-age=7d \
  --max-bytes=1GB \
  --replicas=1
```

**Or via NATS configuration:**

Add to `nats-server.conf`:
```conf
jetstream {
  store_dir: "/data/jetstream"
  max_mem: 256M
  max_file: 1G
}

# Stream definitions (requires NATS 2.10+)
# Note: Alternatively create streams via API/CLI
```

### Step 2: Update PaymentPublisher to Use JetStream

The publisher needs to use JetStream Publish instead of basic Publish.

**Problem:** The `NATS.Client` v1.1.8 library **doesn't support JetStream**.

**Solution:** Upgrade to modern NATS.Net library OR use nats-server CLI for publishing.

#### Option A: Use NATS CLI to Create Stream

```bash
# Install nats CLI in Docker
docker exec -it nats-server-csharp sh

# Inside container (if nats CLI available)
nats stream add PAYMENTS \
  --subjects "payments.>" \
  --storage file \
  --retention limits \
  --max-age=168h

# Or via HTTP API
curl -X POST http://localhost:8222/jsz
```

#### Option B: Create Stream via Script

Create a helper container to set up JetStream:

```yaml
# docker-compose.yml
services:
  jetstream-setup:
    image: natsio/nats-box:latest
    command: |
      sh -c "
        sleep 5 &&
        nats --server nats://nats:4222 stream add PAYMENTS \
          --subjects 'payments.>' \
          --storage file \
          --retention limits \
          --max-age 168h \
          --max-bytes 1GB \
          --replicas 1 \
          --defaults
      "
    depends_on:
      - nats
    networks:
      - nats-network
```

### Step 3: Check if Stream Exists

**Via HTTP API:**
```bash
# Check JetStream status
curl http://localhost:8222/jsz

# List streams
curl -X POST http://localhost:8222/jsm/api/v1/streams
```

**Expected response if stream exists:**
```json
{
  "streams": ["PAYMENTS"],
  "total": 1
}
```

### Step 4: Limitations with Current Library

The **NATS.Client 1.1.8** library is **legacy** and **doesn't support JetStream**.

**To use JetStream, you need:**

1. **Upgrade to NATS.Net v2.x** (modern library)
   - Supports JetStream
   - Different API
   - Requires code rewrite

2. **OR use a different approach:**
   - NATS server mirroring
   - External JetStream publisher
   - NATS CLI-based publishing

## Workaround: Stream Mirroring

If you can't modify the PaymentPublisher, use **stream mirroring**:

### Configure NATS to Mirror Topics

Add to `nats-server.conf`:

```conf
jetstream {
  store_dir: "/data/jetstream"
  max_mem: 256M
  max_file: 1G
}

# Note: Stream mirroring requires configuration via nats CLI or API
# The NATS config file doesn't support stream definitions directly
```

Then create stream via CLI:

```bash
docker exec -it nats-server-csharp sh

# Create stream that captures all payments.* subjects
nats stream add PAYMENTS \
  --subjects "payments.*" \
  --storage file \
  --retention limits \
  --max-age 7d
```

**This will automatically persist all messages** published to `payments.*` subjects, even from basic `Publish()` calls!

## Consuming Historical Messages

Once the stream exists, create a **durable consumer** to replay messages.

### Using nats CLI (Testing)

```bash
# Replay all messages from beginning
docker exec -it nats-server-csharp sh
nats consumer add PAYMENTS REPLAY \
  --deliver all \
  --ack explicit \
  --max-deliver 3 \
  --replay instant

# Read messages
nats consumer next PAYMENTS REPLAY
```

### Using Code (Requires NATS.Net v2.x)

**Not possible with NATS.Client 1.1.8** - would need to upgrade.

With NATS.Net 2.x:
```csharp
var js = nats.CreateJetStreamContext();
var consumer = await js.AddOrUpdateConsumerAsync("PAYMENTS", new ConsumerConfig
{
    Durable = "payment-monitor",
    DeliverPolicy = DeliverPolicy.All,  // Replay from beginning
    AckPolicy = AckPolicy.Explicit
});
```

## Practical Approach for Your Setup

Since you're using the legacy NATS.Client library:

### 1. Enable JetStream on NATS Server ✅ (Already Done)

Your `nats-server.conf` already has:
```conf
jetstream {
  store_dir: "/data/jetstream"
  max_mem: 256M
  max_file: 1G
}
```

### 2. Create Stream to Capture Payment Messages

**Add to docker-compose.yml:**

```yaml
services:
  nats:
    # ... existing config ...

  # Helper to create JetStream stream
  nats-stream-setup:
    image: natsio/nats-box:latest
    container_name: nats-stream-setup
    depends_on:
      nats:
        condition: service_healthy
    networks:
      - nats-network
    command: |
      sh -c "
        echo 'Waiting for NATS...' &&
        sleep 5 &&
        echo 'Creating PAYMENTS stream...' &&
        nats --server nats://nats:4222 stream add PAYMENTS \
          --subjects 'payments.>' \
          --storage file \
          --retention limits \
          --max-age 168h \
          --max-bytes 1G \
          --replicas 1 \
          --discard old \
          --defaults || echo 'Stream may already exist'
      "
    restart: "no"  # Run once
```

### 3. Messages Will Be Persisted Automatically

Once the stream is created, **all messages** published to `payments.*` are automatically persisted, even from basic `Publish()` calls.

### 4. View Historical Messages

**Via CLI:**
```bash
# Enter NATS container
docker exec -it nats-server-csharp sh

# View stream info
nats stream info PAYMENTS

# View messages
nats stream view PAYMENTS
```

**Via HTTP API:**
```bash
# Get stream state
curl -X POST http://localhost:8222/jsm/api/v1/stream/info/PAYMENTS

# Get messages (requires JetStream API calls)
```

### 5. To Replay to MessageLogger

**Problem:** MessageLogger uses `SubscribeAsync()` which **doesn't** replay.

**Solution:** Either:

#### Option A: Manually Query JetStream

```bash
# Replay messages via CLI
nats stream get PAYMENTS 1  # Get message #1
nats stream get PAYMENTS 2  # Get message #2
# etc.
```

#### Option B: Upgrade to NATS.Net 2.x

This requires significant code changes but enables:
- JetStream consumers
- Durable subscriptions
- Message replay
- At-least-once delivery

## Summary Table

| Feature | Basic Pub/Sub | JetStream |
|---------|---------------|-----------|
| **Historical replay** | ❌ No | ✅ Yes |
| **Persistence** | ❌ No | ✅ Yes |
| **Message durability** | ❌ Lost if no subscriber | ✅ Stored on disk |
| **Replay from time** | ❌ No | ✅ Yes |
| **Resume after crash** | ❌ Lose messages | ✅ Resume from last ack |
| **NATS.Client 1.1.8 support** | ✅ Yes | ❌ No (needs v2.x) |
| **Current setup** | ✅ Working | ⚠️ Server supports, client doesn't |

## Recommendation

For **your current setup** with NATS.Client 1.1.8:

### Short Term (No Code Changes)
1. Create JetStream stream (use docker-compose helper above)
2. Messages will be **automatically persisted**
3. View historical messages via `nats` CLI
4. MessageLogger continues with real-time only

### Long Term (Full JetStream Support)
1. Upgrade to **NATS.Net 2.x**
2. Rewrite Publisher to use JetStream publish
3. Rewrite MessageLogger to use JetStream consumers
4. Get full replay, durability, and at-least-once delivery

## Testing Stream Persistence

```bash
# 1. Create stream
docker compose up nats-stream-setup

# 2. Start publisher
docker compose up -d payment-publisher

# 3. Wait for some messages
sleep 30

# 4. Check stream has messages
docker exec -it nats-server-csharp sh
nats stream info PAYMENTS
# Should show: Messages: XX

# 5. View stored messages
nats stream view PAYMENTS --id 1
nats stream view PAYMENTS --id 2

# 6. Even if MessageLogger is stopped, messages are preserved!
docker compose stop payment-monitor
# Messages still in JetStream

# 7. Restart monitor - won't see old messages (needs consumer upgrade)
docker compose start payment-monitor
```

**Bottom line:** JetStream will **store** messages, but to **replay** them to your logger, you need NATS.Net 2.x.
