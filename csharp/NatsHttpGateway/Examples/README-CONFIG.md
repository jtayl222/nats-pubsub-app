# Configuration Guide for NATS Gateway Examples

This guide explains the industry best practices for configuring the NATS HTTP Gateway URL across all example clients.

## Configuration Hierarchy

All examples follow this configuration priority (highest to lowest):

1. **Command-line Arguments** (highest priority)
2. **Environment Variables**
3. **Default Values** (fallback)

This pattern is standard across cloud-native applications and allows flexibility across different environments.

## Quick Reference

### Default Port

The NATS HTTP Gateway runs on **port 5000** by default (not 8080).

```bash
# Gateway default
dotnet run  # Listens on http://localhost:5000
```

### Environment Variable

All examples check the `NATS_GATEWAY_URL` environment variable:

```bash
# Set for current session
export NATS_GATEWAY_URL="http://localhost:5000"

# Set for current command only
NATS_GATEWAY_URL="http://localhost:5000" python3 protobuf_client_example.py
```

### Command-line Argument

All examples accept the URL as the first argument:

```bash
# Python
python3 protobuf_client_example.py http://localhost:5000
python3 websocket_client_example.py http://localhost:5000

# C++
./http_client http://localhost:5000
./websocket_client ws://localhost:5000

# C# (when extracted to standalone project)
dotnet run http://localhost:5000
```

## Usage Examples by Environment

### Development (Local)

**Default behavior** - just run the examples:

```bash
# Start gateway on default port 5000
cd /path/to/NatsHttpGateway
dotnet run

# Examples automatically connect to localhost:5000
python3 protobuf_client_example.py
```

### Development (Custom Port)

If you configured the gateway to use a different port:

```bash
# Start gateway on custom port
ASPNETCORE_URLS="http://localhost:8080" dotnet run

# Option 1: Use environment variable
export NATS_GATEWAY_URL="http://localhost:8080"
python3 protobuf_client_example.py

# Option 2: Use command-line argument
python3 protobuf_client_example.py http://localhost:8080
```

### Docker/Container

Use environment variables in your container:

```bash
# Docker run
docker run -e NATS_GATEWAY_URL="http://gateway:8080" my-client

# Docker Compose
services:
  client:
    image: my-client
    environment:
      - NATS_GATEWAY_URL=http://nats-gateway:8080
```

### Kubernetes

Use ConfigMaps or environment variables:

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: nats-client
spec:
  containers:
  - name: client
    image: my-client
    env:
    - name: NATS_GATEWAY_URL
      value: "http://nats-gateway-service:8080"
```

### Production

Use environment variables from secrets management:

```bash
# From AWS Secrets Manager, Azure Key Vault, etc.
export NATS_GATEWAY_URL=$(aws secretsmanager get-secret-value \
  --secret-id nats-gateway-url \
  --query SecretString \
  --output text)

python3 protobuf_client_example.py
```

## Language-Specific Examples

### Python

```python
import os

# Configuration hierarchy
base_url = (
    sys.argv[1] if len(sys.argv) > 1          # CLI arg
    else os.getenv("NATS_GATEWAY_URL",        # Environment variable
                   "http://localhost:5000")    # Default
)

client = ProtobufClient(base_url)
```

**Usage:**
```bash
# Use default (localhost:5000)
python3 protobuf_client_example.py

# Use environment variable
export NATS_GATEWAY_URL="http://gateway:8080"
python3 protobuf_client_example.py

# Use command-line argument (highest priority)
python3 protobuf_client_example.py http://gateway:8080
```

### C++

```cpp
// Configuration hierarchy
std::string base_url;
if (argc > 1) {
    base_url = argv[1];                          // CLI arg
} else {
    const char* env_url = std::getenv("NATS_GATEWAY_URL");
    base_url = env_url ? env_url                 // Environment variable
                       : "http://localhost:5000"; // Default
}
```

**Usage:**
```bash
# Use default (localhost:5000)
./http_client

# Use environment variable
export NATS_GATEWAY_URL="http://gateway:8080"
./http_client

# Use command-line argument (highest priority)
./http_client http://gateway:8080
```

### C#

```csharp
// Configuration hierarchy
var baseUrl = args.Length > 0                           // CLI arg
    ? args[0]
    : Environment.GetEnvironmentVariable("NATS_GATEWAY_URL")  // Environment variable
      ?? "http://localhost:5000";                        // Default

var client = new ProtobufClientExample(baseUrl);
```

**Usage:**
```bash
# Use default (localhost:5000)
dotnet run

# Use environment variable
export NATS_GATEWAY_URL="http://gateway:8080"
dotnet run

# Use command-line argument (highest priority)
dotnet run http://gateway:8080
```

## Testing the Configuration

### Verify Gateway URL

```bash
# Check where gateway is actually running
curl http://localhost:5000/health

# Or check custom port
curl http://localhost:8080/health
```

### Test Configuration Priority

```bash
# Set environment variable
export NATS_GATEWAY_URL="http://localhost:8080"

# This should use 8080 (from env var)
python3 protobuf_client_example.py

# This should use 9000 (CLI arg overrides env var)
python3 protobuf_client_example.py http://localhost:9000
```

### Debug Connection Issues

If you get connection errors:

1. **Check gateway is running:**
   ```bash
   curl http://localhost:5000/health
   ```

2. **Check NATS is running:**
   ```bash
   nats server check
   ```

3. **Verify port mismatch:**
   ```bash
   # What port is gateway on?
   lsof -i -P | grep LISTEN | grep dotnet

   # What URL are examples using?
   echo $NATS_GATEWAY_URL  # Should match gateway port
   ```

4. **Check firewall/network:**
   ```bash
   # Can you reach the gateway?
   telnet localhost 5000
   ```

## Common Scenarios

### Scenario 1: Gateway on Different Machine

```bash
# Gateway running on 192.168.1.100:8080
export NATS_GATEWAY_URL="http://192.168.1.100:8080"
python3 protobuf_client_example.py
```

### Scenario 2: HTTPS/TLS

```bash
# Gateway with TLS
export NATS_GATEWAY_URL="https://gateway.example.com"
python3 protobuf_client_example.py
```

### Scenario 3: Multiple Environments

```bash
# Create environment-specific config files

# development.env
NATS_GATEWAY_URL=http://localhost:5000

# staging.env
NATS_GATEWAY_URL=http://staging-gateway.internal:8080

# production.env
NATS_GATEWAY_URL=https://gateway.example.com

# Load the appropriate environment
source development.env
python3 protobuf_client_example.py
```

### Scenario 4: CI/CD Pipeline

```yaml
# GitHub Actions example
jobs:
  integration-test:
    runs-on: ubuntu-latest
    env:
      NATS_GATEWAY_URL: http://localhost:8080
    steps:
      - name: Start Gateway
        run: dotnet run &
      - name: Run Tests
        run: python3 protobuf_client_example.py
```

## Best Practices

1. **Never hardcode URLs in production code**
   - Always use environment variables or config files
   - Hardcoded URLs make deployment inflexible

2. **Use sensible defaults for local development**
   - Default to `localhost:5000` (matches gateway default)
   - Developers can run examples without configuration

3. **Document the configuration hierarchy**
   - Make it clear which takes precedence
   - Include examples for each method

4. **Use standard environment variable naming**
   - `{SERVICE}_URL` or `{SERVICE}_BASE_URL` pattern
   - Consistent across all services

5. **Validate URLs at startup**
   - Check connectivity before starting work
   - Provide helpful error messages

6. **Support both HTTP and WebSocket**
   - Convert `http://` to `ws://` automatically
   - Handle `https://` to `wss://` for TLS

## Troubleshooting

### Error: Connection refused to localhost:8080

**Cause:** Gateway is running on port 5000 (default), but examples are trying 8080

**Solution:**
```bash
# Option 1: Use default (examples now default to 5000)
python3 protobuf_client_example.py

# Option 2: Set environment variable
export NATS_GATEWAY_URL="http://localhost:5000"
```

### Error: Environment variable not working

**Cause:** Variable not exported or wrong name

**Solution:**
```bash
# Must use 'export' to make available to child processes
export NATS_GATEWAY_URL="http://localhost:5000"  # ✓ Correct

# Without export, only available in current shell
NATS_GATEWAY_URL="http://localhost:5000"          # ✗ Won't work

# Check if set correctly
echo $NATS_GATEWAY_URL
```

### Error: Command-line argument not overriding

**Cause:** May be passing argument incorrectly

**Solution:**
```bash
# Correct
python3 protobuf_client_example.py http://localhost:8080

# Incorrect (won't work)
NATS_GATEWAY_URL="http://localhost:8080" python3 protobuf_client_example.py http://localhost:5000
# CLI arg takes precedence, will use 5000
```

## Additional Resources

- [12-Factor App Configuration](https://12factor.net/config)
- [Environment Variables in Docker](https://docs.docker.com/engine/reference/commandline/run/#env)
- [Kubernetes ConfigMaps](https://kubernetes.io/docs/concepts/configuration/configmap/)
- [Main Examples README](README.md)
