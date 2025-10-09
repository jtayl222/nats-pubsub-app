# Payment Publisher

Simulates credit card payment processing with random declined transactions for error monitoring.

## Overview

This publisher simulates a payment processing system that:
- Publishes successful credit card transactions to `payments.credit_card.accepted`
- Randomly publishes declined transactions (~1 per minute) to `payments.credit_card.declined`
- Logs declined transactions at **ERROR** level for visibility in Loki
- Does not require a subscriber (fire-and-forget messaging)

## NATS Subjects

- `payments.credit_card.accepted` - Successful transactions (INFO level logs)
- `payments.credit_card.declined` - Failed transactions (ERROR level logs)

## Message Format

### Accepted Transaction
```json
{
  "transaction_id": "TXN-payment-publisher-42",
  "timestamp": "2025-10-09T02:30:15.123Z",
  "source": "payment-publisher",
  "card_type": "Visa",
  "last_four": "4532",
  "amount": 99.99,
  "currency": "USD",
  "status": "accepted",
  "merchant_id": "MERCH-5432",
  "authorization_code": "AUTH-A1B2C3D4"
}
```

### Declined Transaction
```json
{
  "transaction_id": "TXN-payment-publisher-43",
  "timestamp": "2025-10-09T02:31:20.456Z",
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
```

## Decline Reasons

The publisher randomly selects from:
- `insufficient_funds` - Not enough money in account
- `card_expired` - Card past expiration date
- `invalid_cvv` - Security code mismatch
- `suspected_fraud` - Fraud detection triggered
- `card_limit_exceeded` - Over credit limit
- `issuer_declined` - Bank declined for other reasons

## Configuration

### Environment Variables

- `NATS_URL` - NATS server URL (default: `nats://localhost:4222`)
- `HOSTNAME` - Publisher hostname (default: `payment-publisher`)
- `PUBLISH_INTERVAL` - Seconds between transactions (default: `5.0`)

### Docker Compose

```yaml
payment-publisher:
  build: ./PaymentPublisher
  environment:
    - NATS_URL=nats://nats:4222
    - HOSTNAME=payment-publisher
    - PUBLISH_INTERVAL=5.0  # One transaction every 5 seconds
```

## Deployment

### Build and Run

```bash
cd ~/nats-pubsub-app/csharp

# Build and start
docker compose up -d --build payment-publisher

# View logs
docker logs -f payment-publisher
```

### Expected Output

```json
{"timestamp":"...","level":"INFO","message":"Credit card accepted","data":{"transaction_id":"TXN-payment-publisher-1","card_type":"Visa","amount":99.99}}
{"timestamp":"...","level":"INFO","message":"Credit card accepted","data":{"transaction_id":"TXN-payment-publisher-2","card_type":"Amex","amount":49.99}}
{"timestamp":"...","level":"ERROR","message":"Credit card declined","data":{"transaction_id":"TXN-payment-publisher-3","decline_reason":"insufficient_funds"}}
```

## Querying Declined Transactions in Loki

### All Payment Errors

```logql
{container_name="payment-publisher", level="ERROR"}
```

### Declined Transactions with Reason

```logql
{container_name="payment-publisher", level="ERROR"} | json | line_format "{{.data.decline_reason}}: {{.data.amount}}"
```

### Count Declines by Reason

```logql
sum by (decline_reason) (
  count_over_time(
    {container_name="payment-publisher", level="ERROR"} | json [1h]
  )
)
```

### Decline Rate Over Time

```logql
rate({container_name="payment-publisher", level="ERROR"}[5m]) * 100
```

### High-Value Declined Transactions

```logql
{container_name="payment-publisher", level="ERROR"} | json | data_amount > 200
```

### Fraud-Related Declines

```logql
{container_name="payment-publisher"} | json | data_decline_reason="suspected_fraud"
```

## Use Cases

### 1. Error Monitoring
Monitor payment failures in real-time:
```logql
{container_name="payment-publisher", level="ERROR"}
```

### 2. Alerting
Set up Grafana alerts:
- Alert if decline rate > 10% over 5 minutes
- Alert on specific decline reasons (fraud, expired cards)

### 3. Analytics
Track payment trends:
- Acceptance vs. decline rates
- Most common decline reasons
- Average transaction amounts

### 4. No Subscriber Needed
Messages are published to NATS but:
- No application consumes them (fire-and-forget)
- Only logged by the publisher itself
- Can optionally add MessageLogger to capture full payloads

## Adding a Subscriber (Optional)

If you want to process declined transactions:

```yaml
payment-fraud-detector:
  build: ./Subscriber
  environment:
    - NATS_URL=nats://nats:4222
    - NATS_SUBJECT=payments.credit_card.declined
    - HOSTNAME=fraud-detector
```

This subscriber would receive only declined transactions for fraud analysis.

## Metrics

Publisher logs metrics every 20 transactions:

```json
{
  "level": "INFO",
  "message": "Payment publisher metrics",
  "data": {
    "total_transactions": 100,
    "accepted": 85,
    "declined": 15,
    "decline_rate": 15.0,
    "uptime_seconds": 500,
    "transactions_per_second": 0.2
  }
}
```

Query metrics in Loki:
```logql
{container_name="payment-publisher"} |= "Payment publisher metrics" | json
```

## Troubleshooting

### No Declined Transactions Appearing

Check time range in Grafana - declines happen ~1 per minute:
- Set time range to "Last 5 minutes"
- Wait at least 60 seconds after starting

### Publisher Not Connecting

```bash
# Check NATS is running
docker logs nats-server-csharp

# Check publisher logs
docker logs payment-publisher

# Verify network
docker exec payment-publisher ping -c 1 nats
```

### Adjust Decline Frequency

Edit `PUBLISH_INTERVAL` to control transaction rate:
```yaml
environment:
  - PUBLISH_INTERVAL=2.0  # Faster transactions = more frequent errors
```

With 2-second interval: ~30 declines per hour
With 5-second interval: ~12 declines per hour
With 10-second interval: ~6 declines per hour
