# Payment Monitoring Setup

Monitor remote payment processing systems via NATS topics without access to application logs.

## Scenario

```
┌─────────────────────────────────────┐
│   Remote Payment Server             │
│   (No console access)               │
│                                     │
│   ┌───────────────────┐            │
│   │ PaymentPublisher  │            │
│   │ - Processes cards │            │
│   │ - Logging: NONE   │            │
│   └─────────┬─────────┘            │
│             │                       │
│             │ publishes to NATS     │
│             ▼                       │
└─────────────┼───────────────────────┘
              │
              │ payments.credit_card.accepted
              │ payments.credit_card.declined
              ▼
       ┌──────────────┐
       │  NATS Server │
       │              │
       └──────┬───────┘
              │
              │ subscribes
              ▼
    ┌───────────────────┐
    │  Payment Monitor  │
    │  (MessageLogger)  │
    │  - Logs to stdout │
    └─────────┬─────────┘
              │
              ▼
        ┌─────────┐
        │ Promtail│
        └────┬────┘
             │
             ▼
        ┌────────┐      ┌─────────┐
        │  Loki  │◄─────│ Grafana │
        └────────┘      └─────────┘
```

## Key Points

1. **PaymentPublisher** runs on remote server
   - You have NO access to its logs (`logging: driver: none`)
   - It only publishes messages to NATS

2. **PaymentMonitor** (MessageLogger) subscribes to NATS topics
   - Receives all payment messages
   - Logs accepted transactions as **INFO**
   - Logs declined transactions as **ERROR**
   - These logs go to Loki via Promtail

3. **You query Loki** to see what's happening on the remote server

## Deployment

### 1. Deploy the Stack

```bash
cd ~/nats-pubsub-app/csharp

# Build and start both services
docker compose up -d --build payment-publisher payment-monitor

# Verify they're running
docker compose ps
```

### 2. Verify Payment Publisher (No Logs!)

```bash
# Try to view logs - SHOULD BE EMPTY
docker logs payment-publisher
# Output: (nothing - logging disabled)

# But it IS running and publishing to NATS
docker inspect payment-publisher | grep -A 3 State
```

### 3. Verify Payment Monitor (Has Logs!)

```bash
# View monitor logs - captures NATS messages
docker logs -f payment-monitor

# You'll see:
# {"level":"INFO","message":"Payment transaction captured","data":{"subject":"payments.credit_card.accepted",...}}
# {"level":"ERROR","message":"Payment transaction declined","data":{"decline_reason":"insufficient_funds",...}}
```

## Querying in Grafana

### All Payment Transactions (Accepted + Declined)

```logql
{container_name="payment-monitor"}
```

### Only Declined Transactions (Errors)

```logql
{container_name="payment-monitor", level="ERROR"}
```

### Declined with Reason and Amount

```logql
{container_name="payment-monitor", level="ERROR"}
| json
| line_format "{{.data.decline_reason}}: ${{.data.amount}} ({{.data.card_type}} {{.data.transaction_id}})"
```

### Count Declines by Reason

```logql
sum by (decline_reason) (
  count_over_time(
    {container_name="payment-monitor", level="ERROR"}
    | json
    | unwrap data_decline_reason [1h]
  )
)
```

### Accepted Transactions Only

```logql
{container_name="payment-monitor"} | json | data_status="accepted"
```

### High-Value Transactions (> $200)

```logql
{container_name="payment-monitor"}
| json
| data_amount > 200
```

### Fraud-Related Declines

```logql
{container_name="payment-monitor", level="ERROR"}
| json
| data_decline_reason="suspected_fraud"
```

### Transaction Rate (Accepted vs Declined)

```logql
sum by (status) (
  rate({container_name="payment-monitor"} | json [5m])
)
```

## Log Format

### Accepted Transaction Log
```json
{
  "timestamp": "2025-10-09T03:15:22.123Z",
  "level": "INFO",
  "logger": "nats-message-logger",
  "message": "Payment transaction captured",
  "hostname": "payment-monitor",
  "data": {
    "subject": "payments.credit_card.accepted",
    "size_bytes": 245,
    "transaction_id": "TXN-payment-publisher-42",
    "status": "accepted",
    "amount": 99.99,
    "card_type": "Visa",
    "payload": {
      "transaction_id": "TXN-payment-publisher-42",
      "timestamp": "2025-10-09T03:15:22.120Z",
      "source": "payment-publisher",
      "card_type": "Visa",
      "last_four": "4532",
      "amount": 99.99,
      "currency": "USD",
      "status": "accepted",
      "merchant_id": "MERCH-5432",
      "authorization_code": "AUTH-A1B2C3D4"
    }
  }
}
```

### Declined Transaction Log (ERROR level)
```json
{
  "timestamp": "2025-10-09T03:16:35.456Z",
  "level": "ERROR",
  "logger": "nats-message-logger",
  "message": "Payment transaction declined",
  "hostname": "payment-monitor",
  "data": {
    "subject": "payments.credit_card.declined",
    "size_bytes": 268,
    "transaction_id": "TXN-payment-publisher-47",
    "status": "declined",
    "decline_reason": "insufficient_funds",
    "amount": 249.99,
    "card_type": "Mastercard",
    "payload": {
      "transaction_id": "TXN-payment-publisher-47",
      "timestamp": "2025-10-09T03:16:35.450Z",
      "source": "payment-publisher",
      "card_type": "Mastercard",
      "last_four": "8765",
      "amount": 249.99,
      "currency": "USD",
      "status": "declined",
      "decline_reason": "insufficient_funds",
      "merchant_id": "MERCH-1234",
      "decline_code": "ERR-451"
    }
  }
}
```

## What You Get

### Without NATS Monitoring
- ❌ No visibility into remote server
- ❌ Can't see transaction failures
- ❌ No alerting on errors
- ❌ No analytics

### With NATS Monitoring
- ✅ Full visibility via Loki
- ✅ Declined transactions logged as ERROR
- ✅ Can set up Grafana alerts
- ✅ Query transaction history
- ✅ Analyze decline patterns
- ✅ Monitor in real-time

## Grafana Alerts

### Alert on High Decline Rate

Create an alert in Grafana:

**Query:**
```logql
(
  sum(rate({container_name="payment-monitor", level="ERROR"}[5m]))
  /
  sum(rate({container_name="payment-monitor"}[5m]))
) * 100 > 10
```

**Condition:** Decline rate > 10% for 5 minutes

**Action:** Send notification to Slack/email

### Alert on Fraud

**Query:**
```logql
count_over_time(
  {container_name="payment-monitor"}
  | json
  | data_decline_reason="suspected_fraud" [1m]
) > 0
```

**Condition:** Any fraud detected

**Action:** Immediate notification

## Real-World Use Cases

### 1. Third-Party Payment Processor
Your payment provider only exposes NATS topics, not logs:
- Subscribe to their topics
- Log all transactions
- Monitor decline rates
- Alert on anomalies

### 2. Microservices Architecture
Payment service runs in another cluster:
- No direct log access
- Only NATS communication
- Monitor via topics
- Centralized logging in Loki

### 3. Legacy System Integration
Old payment system can't be modified:
- Publishes to NATS
- Can't add logging
- Monitor externally via NATS
- Modern observability for legacy system

## Testing

### Verify Everything Works

```bash
# 1. Check payment-publisher is running (but no logs)
docker ps | grep payment-publisher
docker logs payment-publisher  # Should be empty

# 2. Check payment-monitor is capturing messages
docker logs payment-monitor | grep "Payment transaction"

# 3. Wait ~60 seconds for first decline
sleep 60
docker logs payment-monitor | grep "ERROR"

# 4. Query Loki via API
curl -s "http://localhost:3100/loki/api/v1/query" \
  --data-urlencode 'query={container_name="payment-monitor"}' \
  | jq '.data.result[0].values'
```

### Simulate Remote Server

To truly simulate a remote server:

```yaml
# docker-compose.yml
payment-publisher:
  # ... existing config ...

  # Add network isolation (optional)
  networks:
    - payments-internal  # Different network, no external access
```

## Scaling

### Multiple Payment Servers

Monitor multiple remote payment systems:

```yaml
payment-monitor-region1:
  build: ./MessageLogger
  environment:
    - NATS_URL=nats://nats-region1:4222
    - NATS_SUBJECT=payments.>
    - HOSTNAME=payment-monitor-region1

payment-monitor-region2:
  build: ./MessageLogger
  environment:
    - NATS_URL=nats://nats-region2:4222
    - NATS_SUBJECT=payments.>
    - HOSTNAME=payment-monitor-region2
```

Query both in Grafana:
```logql
{component="payment-monitor"}  # All regions
{hostname="payment-monitor-region1"}  # Specific region
```

## Summary

- **PaymentPublisher**: Simulates remote server (no log access)
- **PaymentMonitor**: Subscribes to NATS topics, logs to Loki
- **Monitor only via NATS**: Real-world scenario
- **Declined transactions**: Logged as ERROR, queryable in Grafana
- **No subscriber needed**: Monitor-only pattern
- **Perfect for**: Third-party systems, legacy integrations, microservices
