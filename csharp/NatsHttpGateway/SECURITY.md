# Security Configuration Guide

This guide covers the dual security layers implemented in the NATS HTTP Gateway:

1. **REST API Layer**: JWT Bearer token authentication
2. **NATS Connection Layer**: mTLS (mutual TLS) with certificates

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [JWT Authentication (REST API)](#jwt-authentication-rest-api)
- [mTLS Configuration (NATS)](#mtls-configuration-nats)
- [Generating Test Certificates](#generating-test-certificates)
- [Generating JWT Tokens](#generating-jwt-tokens)
- [Running with Security Enabled](#running-with-security-enabled)
- [Testing Security](#testing-security)
- [Troubleshooting](#troubleshooting)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        Client                                    │
└─────────────────────────┬───────────────────────────────────────┘
                          │ HTTP + JWT Bearer Token
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                   NATS HTTP Gateway                              │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │              JWT Authentication Middleware               │    │
│  │         (validates tokens from external IdP)            │    │
│  └─────────────────────────────────────────────────────────┘    │
│                          │                                       │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    Controllers                           │    │
│  │   [Authorize] - Messages, Streams, Consumers, WebSocket │    │
│  │   [AllowAnonymous] - Health                             │    │
│  └─────────────────────────────────────────────────────────┘    │
│                          │                                       │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    NatsService                           │    │
│  │              (mTLS client certificate)                   │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────┬───────────────────────────────────────┘
                          │ TLS + Client Certificate
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                     NATS Server                                  │
│                (requires client cert verification)               │
└─────────────────────────────────────────────────────────────────┘
```

---

## JWT Authentication (REST API)

### Overview

JWT authentication protects all API endpoints except `/health`. When enabled:
- Requests without a valid `Authorization: Bearer <token>` header receive `401 Unauthorized`
- The gateway validates tokens but does NOT issue them (validate-only mode)
- Tokens must be obtained from your identity provider (Auth0, Keycloak, Azure AD, etc.)

### Configuration

| Environment Variable | Description | Required |
|---------------------|-------------|----------|
| `JWT_KEY` | Symmetric signing key (min 32 chars for HS256) | Yes (to enable) |
| `JWT_ISSUER` | Expected token issuer (e.g., `https://your-idp.com`) | No |
| `JWT_AUDIENCE` | Expected token audience (e.g., `nats-gateway`) | No |

**Note:** If `JWT_KEY` is not set, JWT authentication is disabled and all endpoints are public.

### Protected vs Public Endpoints

| Endpoint | Authentication |
|----------|---------------|
| `GET /health` | Public (`[AllowAnonymous]`) |
| `POST /api/messages/{subject}` | Protected (`[Authorize]`) |
| `GET /api/messages/{subject}` | Protected (`[Authorize]`) |
| `GET /api/streams` | Protected (`[Authorize]`) |
| `GET /api/streams/{name}` | Protected (`[Authorize]`) |
| `GET /api/consumers/{stream}` | Protected (`[Authorize]`) |
| `POST /api/consumers/{stream}` | Protected (`[Authorize]`) |
| `WS /ws/websocketmessages/*` | Protected (`[Authorize]`) |
| `POST /api/proto/messages/*` | Protected (`[Authorize]`) |

### Example Request

```bash
# Without token - returns 401
curl http://localhost:5000/api/streams
# Response: 401 Unauthorized

# With valid token - returns 200
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..." http://localhost:5000/api/streams
# Response: {"count":0,"streams":[]}
```

---

## mTLS Configuration (NATS)

### Overview

mTLS (mutual TLS) secures the connection between the gateway and NATS server:
- The gateway presents a client certificate to NATS
- NATS verifies the certificate against its trusted CA
- All traffic is encrypted

### Configuration

| Environment Variable | Description | Required |
|---------------------|-------------|----------|
| `NATS_URL` | NATS server URL (e.g., `nats://nats.example.com:4222`) | Yes |
| `NATS_CA_FILE` | Path to CA certificate (PEM format) | For TLS |
| `NATS_CERT_FILE` | Path to client certificate (PEM format) | For mTLS |
| `NATS_KEY_FILE` | Path to client private key (PEM format) | For mTLS |

### Connection Modes

1. **Plain NATS** (no TLS): Only set `NATS_URL`
2. **TLS with CA verification**: Set `NATS_URL` + `NATS_CA_FILE`
3. **Full mTLS**: Set all four variables

---

## Generating Test Certificates

For development and testing, generate self-signed certificates:

### 1. Create Certificate Directory

```bash
mkdir -p test-certs && cd test-certs
```

### 2. Generate Root CA

```bash
# Generate CA private key (4096-bit)
openssl genrsa -out rootCA.key 4096

# Generate CA certificate (valid 365 days)
openssl req -x509 -new -nodes -key rootCA.key -sha256 -days 365 -out rootCA.pem \
  -subj "/C=US/ST=State/L=City/O=Organization/CN=TestRootCA"
```

### 3. Generate Client Certificate

```bash
# Generate client private key (2048-bit)
openssl genrsa -out client.key 2048

# Generate certificate signing request (CSR)
openssl req -new -key client.key -out client.csr \
  -subj "/C=US/ST=State/L=City/O=Organization/CN=nats-gateway-client"

# Sign with CA (valid 365 days)
openssl x509 -req -in client.csr -CA rootCA.pem -CAkey rootCA.key -CAcreateserial \
  -out client.crt -days 365 -sha256

# Cleanup CSR
rm client.csr
```

### 4. Generate NATS Server Certificate (if needed)

```bash
# Generate server private key
openssl genrsa -out server.key 2048

# Generate server CSR with SAN for localhost
cat > server.cnf << EOF
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
IP.1 = 127.0.0.1
EOF

openssl req -new -key server.key -out server.csr -config server.cnf

# Sign server certificate
openssl x509 -req -in server.csr -CA rootCA.pem -CAkey rootCA.key -CAcreateserial \
  -out server.crt -days 365 -sha256 -extensions v3_req -extfile server.cnf

rm server.csr server.cnf
```

### Generated Files

```
test-certs/
├── rootCA.key      # CA private key (keep secure!)
├── rootCA.pem      # CA certificate (distribute to clients/servers)
├── client.key      # Gateway client private key
├── client.crt      # Gateway client certificate
├── server.key      # NATS server private key (optional)
└── server.crt      # NATS server certificate (optional)
```

---

## Generating JWT Tokens

### Using OpenSSL (Bash)

Create a script `generate-jwt.sh`:

```bash
#!/bin/bash
# Generate a JWT token for testing

JWT_KEY="${1:-test-secret-key-for-jwt-validation-min-32-chars}"
ISSUER="${2:-test-issuer}"
AUDIENCE="${3:-nats-gateway}"

# Base64url encode function
base64url_encode() {
    openssl base64 -e -A | tr '+/' '-_' | tr -d '='
}

# Timestamps
IAT=$(date +%s)
EXP=$((IAT + 86400))  # 24 hours

# JWT Header
HEADER='{"alg":"HS256","typ":"JWT"}'
HEADER_B64=$(echo -n "$HEADER" | base64url_encode)

# JWT Payload
PAYLOAD="{\"sub\":\"test-user\",\"name\":\"Test User\",\"iss\":\"$ISSUER\",\"aud\":\"$AUDIENCE\",\"iat\":$IAT,\"exp\":$EXP}"
PAYLOAD_B64=$(echo -n "$PAYLOAD" | base64url_encode)

# Signature
SIGNATURE=$(echo -n "${HEADER_B64}.${PAYLOAD_B64}" | openssl dgst -sha256 -hmac "$JWT_KEY" -binary | base64url_encode)

# Output
echo "JWT_KEY=$JWT_KEY"
echo "JWT_ISSUER=$ISSUER"
echo "JWT_AUDIENCE=$AUDIENCE"
echo "API_TOKEN=${HEADER_B64}.${PAYLOAD_B64}.${SIGNATURE}"
echo ""
echo "# Expires: $(date -r $EXP '+%Y-%m-%d %H:%M:%S')"
```

Usage:
```bash
chmod +x generate-jwt.sh
./generate-jwt.sh "your-secret-key" "your-issuer" "your-audience"
```

### Using .NET

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("your-secret-key-min-32-chars"));
var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

var token = new JwtSecurityToken(
    issuer: "your-issuer",
    audience: "nats-gateway",
    claims: new[] {
        new Claim(ClaimTypes.Name, "test-user"),
        new Claim(ClaimTypes.Role, "admin")
    },
    expires: DateTime.UtcNow.AddHours(24),
    signingCredentials: creds
);

var jwt = new JwtSecurityTokenHandler().WriteToken(token);
Console.WriteLine(jwt);
```

### Using jwt.io (Manual)

1. Go to [jwt.io](https://jwt.io)
2. Select algorithm: HS256
3. Edit payload:
   ```json
   {
     "sub": "test-user",
     "iss": "your-issuer",
     "aud": "nats-gateway",
     "iat": 1704067200,
     "exp": 1704153600
   }
   ```
4. Enter your secret key
5. Copy the encoded token

---

## Running with Security Enabled

### Option 1: Environment Variables

```bash
# JWT Authentication
export JWT_KEY="your-secret-key-minimum-32-characters-long"
export JWT_ISSUER="https://your-idp.com"
export JWT_AUDIENCE="nats-gateway"

# NATS mTLS
export NATS_URL="nats://nats.example.com:4222"
export NATS_CA_FILE="/path/to/rootCA.pem"
export NATS_CERT_FILE="/path/to/client.crt"
export NATS_KEY_FILE="/path/to/client.key"

dotnet run
```

### Option 2: Using test-env.sh

```bash
# Source the test environment (from test-certs directory)
source test-certs/test-env.sh

dotnet run
```

### Option 3: Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

# Security configuration
ENV JWT_KEY="your-secret-key"
ENV JWT_ISSUER="your-issuer"
ENV JWT_AUDIENCE="nats-gateway"
ENV NATS_URL="nats://nats:4222"
ENV NATS_CA_FILE="/certs/rootCA.pem"
ENV NATS_CERT_FILE="/certs/client.crt"
ENV NATS_KEY_FILE="/certs/client.key"

ENTRYPOINT ["dotnet", "NatsHttpGateway.dll"]
```

---

## Testing Security

### Verify JWT Enforcement

```bash
# 1. Health endpoint (public) - should return 200
curl -w "\nStatus: %{http_code}\n" http://localhost:5000/health

# 2. Protected endpoint without token - should return 401
curl -w "\nStatus: %{http_code}\n" http://localhost:5000/api/streams

# 3. Protected endpoint with valid token - should return 200
curl -w "\nStatus: %{http_code}\n" \
  -H "Authorization: Bearer $API_TOKEN" \
  http://localhost:5000/api/streams

# 4. Protected endpoint with invalid token - should return 401
curl -w "\nStatus: %{http_code}\n" \
  -H "Authorization: Bearer invalid.token.here" \
  http://localhost:5000/api/streams
```

### Run Component Tests with Security

```bash
# Set security environment
export JWT_KEY="test-secret-key-for-jwt-validation-min-32-chars"
export JWT_ISSUER="test-issuer"
export JWT_AUDIENCE="nats-gateway"
export API_TOKEN="<your-generated-token>"

# For mTLS (optional)
export NATS_CA_FILE="/path/to/rootCA.pem"
export NATS_CERT_FILE="/path/to/client.crt"
export NATS_KEY_FILE="/path/to/client.key"

# Run tests
dotnet test NatsHttpGateway.ComponentTests --filter "Category=Component"
```

### Run Security-Specific Tests

```bash
dotnet test NatsHttpGateway.ComponentTests --filter "Category=Security"
dotnet test NatsHttpGateway.Tests --filter "Category=Security"
```

---

## Troubleshooting

### JWT Authentication Issues

| Error | Cause | Solution |
|-------|-------|----------|
| `401 Unauthorized` | Missing or invalid token | Check `Authorization: Bearer <token>` header |
| `401 - IDX10214` | Token signature invalid | Verify `JWT_KEY` matches signing key |
| `401 - IDX10223` | Token expired | Generate a new token with future `exp` |
| `401 - IDX10205` | Issuer mismatch | Check `JWT_ISSUER` matches token's `iss` |
| `401 - IDX10206` | Audience mismatch | Check `JWT_AUDIENCE` matches token's `aud` |

### mTLS Connection Issues

| Error | Cause | Solution |
|-------|-------|----------|
| `FileNotFoundException` | Certificate file not found | Verify file paths are correct |
| `AuthenticationException` | Certificate rejected | Verify cert is signed by CA trusted by NATS |
| `Connection refused` | NATS not running or wrong port | Check `NATS_URL` and server status |
| `TLS handshake failed` | Certificate/key mismatch | Regenerate matching cert and key |

### Viewing Logs

```bash
# Run with detailed logging
ASPNETCORE_ENVIRONMENT=Development dotnet run

# Check for security-related log entries:
# - "JWT authentication enabled"
# - "JWT authentication failed"
# - "NATS mTLS enabled"
# - "Loaded CA certificate"
# - "Loaded client certificate"
```

### Verifying Certificate Chain

```bash
# Verify client cert is signed by CA
openssl verify -CAfile rootCA.pem client.crt

# View certificate details
openssl x509 -in client.crt -text -noout

# Test TLS connection to NATS
openssl s_client -connect localhost:4222 -CAfile rootCA.pem -cert client.crt -key client.key
```

---

## Security Considerations

### Production Recommendations

1. **JWT Key Management**
   - Use a strong, random key (256+ bits)
   - Store in a secrets manager (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault)
   - Rotate keys periodically

2. **Certificate Management**
   - Use certificates from a trusted CA in production
   - Set appropriate certificate lifetimes
   - Implement certificate rotation before expiry
   - Monitor certificate expiration

3. **Network Security**
   - Use HTTPS for the REST API (terminate TLS at load balancer or gateway)
   - Restrict NATS access to internal networks
   - Use network policies in Kubernetes

4. **Monitoring**
   - Log authentication failures
   - Alert on repeated 401/403 responses
   - Monitor certificate expiration dates

### What This Implementation Does NOT Provide

- **Token issuance**: Tokens must come from an external IdP
- **Role-based authorization**: All authenticated users have equal access
- **API rate limiting**: Should be implemented at load balancer level
- **Input validation beyond basic checks**: Validate payloads as needed
