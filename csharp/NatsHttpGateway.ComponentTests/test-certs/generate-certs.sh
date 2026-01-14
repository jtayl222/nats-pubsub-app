#!/bin/bash
# Generate test certificates for component tests
# These are TEST-ONLY certificates - DO NOT use in production
#
# Creates:
#   - rootCA.pem/key    - Certificate Authority
#   - server.pem/key    - NATS server certificate
#   - client.pem/key    - Gateway client certificate

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Generating Test Certificates ==="
echo "WARNING: These certificates are for TESTING ONLY"
echo ""

# Clean up any existing certificates
rm -f *.pem *.key *.csr *.srl *.cnf 2>/dev/null || true

echo "[1/3] Generating Root CA..."
openssl genrsa -out rootCA.key 2048 2>/dev/null
openssl req -x509 -new -nodes -key rootCA.key -sha256 -days 3650 \
    -out rootCA.pem \
    -subj "/C=US/ST=Test/L=Test/O=ComponentTestCA/CN=Test Root CA" 2>/dev/null

echo "[2/3] Generating NATS Server certificate..."
openssl genrsa -out server.key 2048 2>/dev/null

# Create server certificate config with SANs
cat > server.cnf << 'EOF'
[req]
distinguished_name = req_distinguished_name
req_extensions = v3_req
prompt = no

[req_distinguished_name]
CN = nats-server

[v3_req]
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
DNS.2 = nats
DNS.3 = nats-server
DNS.4 = nats-tls-test
IP.1 = 127.0.0.1
EOF

openssl req -new -key server.key -out server.csr -config server.cnf 2>/dev/null
openssl x509 -req -in server.csr -CA rootCA.pem -CAkey rootCA.key \
    -CAcreateserial -out server.pem -days 3650 -sha256 \
    -extensions v3_req -extfile server.cnf 2>/dev/null

echo "[3/3] Generating Client certificate..."
openssl genrsa -out client.key 2048 2>/dev/null
openssl req -new -key client.key -out client.csr \
    -subj "/C=US/ST=Test/L=Test/O=ComponentTestClient/CN=nats-gateway-client" 2>/dev/null
openssl x509 -req -in client.csr -CA rootCA.pem -CAkey rootCA.key \
    -CAcreateserial -out client.pem -days 3650 -sha256 2>/dev/null

# Cleanup temporary files
rm -f *.csr *.srl *.cnf

echo ""
echo "=== Certificates Generated Successfully ==="
echo ""
ls -la *.pem *.key
echo ""
echo "Files:"
echo "  rootCA.pem  - CA certificate (trust anchor)"
echo "  rootCA.key  - CA private key"
echo "  server.pem  - NATS server certificate"
echo "  server.key  - NATS server private key"
echo "  client.pem  - Client certificate"
echo "  client.key  - Client private key"
