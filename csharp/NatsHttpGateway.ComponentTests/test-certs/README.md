# Test Certificates

**WARNING: These certificates are for TESTING ONLY. Do not use in production.**

## Contents

| File | Purpose |
|------|---------|
| `rootCA.pem` | Certificate Authority (trust anchor) |
| `rootCA.key` | CA private key (for signing) |
| `server.pem` | NATS server certificate |
| `server.key` | NATS server private key |
| `client.pem` | Gateway client certificate |
| `client.key` | Gateway client private key |
| `nats-server.conf` | NATS TLS configuration |

## Regenerating Certificates

If certificates expire or need regeneration:

```bash
./generate-certs.sh
```

## Certificate Details

- **Validity:** 10 years (test-only, long validity for convenience)
- **Key size:** 2048-bit RSA
- **Server SANs:** localhost, nats, nats-server, nats-tls-test, 127.0.0.1

## Usage

### Local Development

```bash
docker run -d --name nats-tls \
  -p 4222:4222 -p 8222:8222 \
  -v $(pwd):/certs:ro \
  nats:latest -c /certs/nats-server.conf
```

### Environment Variables

```bash
export NATS_URL="tls://localhost:4222"
export NATS_CA_FILE="$(pwd)/rootCA.pem"
export NATS_CERT_FILE="$(pwd)/client.pem"
export NATS_KEY_FILE="$(pwd)/client.key"
```
