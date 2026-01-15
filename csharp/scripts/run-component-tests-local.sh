#!/bin/bash
# Run component tests locally with mTLS-enabled NATS
# This script handles certificate generation, NATS startup, and cleanup
# Uses ports 4333/8333 to avoid conflicts with production NATS on 4222/8222

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
CERTS_DIR="$PROJECT_DIR/NatsHttpGateway.ComponentTests/test-certs"
CONTAINER_NAME="nats-component-test"

cleanup() {
    echo "Cleaning up..."
    docker stop $CONTAINER_NAME 2>/dev/null || true
    docker rm $CONTAINER_NAME 2>/dev/null || true
}

# Set trap for cleanup on exit
trap cleanup EXIT

echo "=== Component Tests with mTLS ==="
echo ""

# Check if certificates exist, generate if not
if [ ! -f "$CERTS_DIR/rootCA.pem" ]; then
    echo "Generating test certificates..."
    (cd "$CERTS_DIR" && ./generate-certs.sh)
    echo ""
fi

# Stop any existing container
docker stop $CONTAINER_NAME 2>/dev/null || true
docker rm $CONTAINER_NAME 2>/dev/null || true

# Start NATS with TLS
echo "Starting NATS with mTLS..."
docker run -d --name $CONTAINER_NAME \
    -p 4333:4222 -p 8333:8222 \
    -v "$CERTS_DIR:/certs:ro" \
    nats:latest \
    -c /certs/nats-server.conf

# Wait for NATS to be ready
echo "Waiting for NATS to be ready..."
for i in {1..30}; do
    if curl -s http://localhost:8333/healthz > /dev/null 2>&1; then
        echo "NATS is ready!"
        break
    fi
    if [ $i -eq 30 ]; then
        echo "NATS failed to start within 30 seconds"
        docker logs $CONTAINER_NAME
        exit 1
    fi
    sleep 1
done

echo ""
echo "Running component tests..."
echo ""

# Set environment variables
export NATS_URL="tls://localhost:4333"
export NATS_CA_FILE="$CERTS_DIR/rootCA.pem"
export NATS_CERT_FILE="$CERTS_DIR/client.pem"
export NATS_KEY_FILE="$CERTS_DIR/client.key"

# Run tests
cd "$PROJECT_DIR"
dotnet test NatsHttpGateway.ComponentTests/NatsHttpGateway.ComponentTests.csproj \
    --filter "Category=Component" \
    --logger "console;verbosity=detailed"

echo ""
echo "=== Tests completed ==="
