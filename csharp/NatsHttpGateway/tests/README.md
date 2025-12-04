# NATS HTTP Gateway Test Tools

This directory contains test tools for the NATS HTTP Gateway Consumer APIs.

## Tools

### 1. Interactive CLI Tester (Recommended)

**File:** `consumer_interactive_tester.py`

A simple command-line menu for testing Message and Consumer API endpoints interactively.

**Features:**
- Numbered menu of all endpoints (1-12) - labels match Swagger descriptions
- Publish messages to test with
- Fetch messages using ephemeral or durable consumers
- Edit parameters before each request (stream, consumer, subject, etc.)
- See curl command before executing
- Color-coded output for easy reading
- Works everywhere - no GUI dependencies

**Usage:**
```bash
# Run with defaults
python tests/consumer_interactive_tester.py

# Or set custom defaults
GATEWAY_BASE_URL=http://localhost:8080 \
GATEWAY_STREAM=events \
GATEWAY_SUBJECT=events.demo \
python tests/consumer_interactive_tester.py
```

**Requirements:**
- Python 3.7+
- `requests` library: `pip install requests`

**Environment Variables:**
- `GATEWAY_BASE_URL` - Gateway URL (default: `http://localhost:8080`)
- `GATEWAY_STREAM` - Default stream name (default: `events`)
- `GATEWAY_SUBJECT` - Default subject (default: `events.demo`)

**Menu Options:**
- `1-12` - Select API endpoint to test
- `c` - Change configuration (URL, stream, subject, consumer)
- `q` - Quit

---

### 2. Automated UAT Script

**File:** `consumer_uat.py`

Sequential automated test that exercises all Consumer API endpoints in a specific order.

**Features:**
- Runs through complete consumer lifecycle
- Tests JSON and Protobuf endpoints
- Generates detailed log file
- Supports unattended CI/CD runs

**Usage:**
```bash
# Interactive mode (pauses between steps)
python tests/consumer_uat.py

# Automated mode (no pauses)
GATEWAY_AUTO_ADVANCE=true python tests/consumer_uat.py
```

**Requirements:**
- Python 3.7+
- `requests` library: `pip install requests`
- `protobuf` library: `pip install protobuf`
- Generated protobuf stubs (see main README)

**Environment Variables:**
- `GATEWAY_BASE_URL` - Gateway URL (default: `http://localhost:8080`)
- `GATEWAY_STREAM` - Stream name (default: `events`)
- `GATEWAY_SUBJECT` - Subject to test (default: `events.demo`)
- `GATEWAY_CONSUMER` - Consumer name (auto-generated if not set)
- `GATEWAY_AUTO_ADVANCE` - Skip pauses (default: `false`)
- `GATEWAY_PUBLISH_COUNT` - Messages to publish (default: `3`)
- `GATEWAY_UAT_LOG` - Log file path (default: `consumer_uat_log.json`)

**Output:**
Creates `consumer_uat_log.json` with detailed results from each test step.

---

## Quick Start

### For Manual Testing (Interactive CLI)

Best for exploring APIs and debugging:

```bash
# Install dependencies
pip install requests

# Run interactive tester
python tests/consumer_interactive_tester.py
```

1. Type a number (1-12) to select an endpoint
2. Edit parameters if needed (or press Enter to use defaults)
3. Review the curl command
4. Press Enter to execute
5. View the color-coded response
6. Press Enter to return to menu

### For Automated Testing (UAT)

Best for CI/CD and regression testing:

```bash
# Install dependencies
pip install requests protobuf

# Generate protobuf stubs (one-time)
python -m grpc_tools.protoc -I=Protos --python_out=Examples Protos/message.proto

# Run UAT
GATEWAY_AUTO_ADVANCE=true python tests/consumer_uat.py
```

---

## API Endpoints Tested

### Message Endpoints (Interactive CLI Tester)

1. **Publish a message to a NATS subject** - `POST /api/messages/{subject}`
2. **Fetch the last N messages from a NATS subject using an ephemeral consumer** - `GET /api/messages/{subjectFilter}`
3. **Fetch messages from a NATS stream using a durable (well-known) consumer** - `GET /api/messages/{stream}/consumer/{consumerName}`

### Consumer Endpoints (Both Tools)

1. **Get predefined consumer templates** - `GET /api/consumers/templates`
2. **List all consumers for a stream** - `GET /api/consumers/{stream}`
3. **Get detailed information about a specific consumer** - `GET /api/consumers/{stream}/{consumer}`
4. **Create a new consumer on a stream** - `POST /api/consumers/{stream}`
5. **Check the health status of a consumer** - `GET /api/consumers/{stream}/{consumer}/health`
6. **Peek at messages from a consumer without acknowledging them** - `GET /api/consumers/{stream}/{consumer}/messages`
7. **Reset or replay messages from a consumer** - `POST /api/consumers/{stream}/{consumer}/reset`
8. **Get metrics history for a consumer** - `GET /api/consumers/{stream}/{consumer}/metrics/history`
9. **Delete a consumer from a stream** - `DELETE /api/consumers/{stream}/{consumer}`

---

## Troubleshooting

### Gateway not responding
- Check gateway is running: `curl http://localhost:8080/Health`
- Verify NATS is running: `docker ps` or check NATS logs
- Check firewall/port forwarding

### Stream not found
- Create stream via NATS CLI: `nats stream add events`
- Or publish a message to auto-create: `POST /api/messages/events.test`

### Protobuf errors (UAT script)
- Install protobuf: `pip install protobuf`
- Generate stubs: See main README for instructions
- Check Python path includes `Examples` directory

---

## Choosing the Right Tool

| Scenario | Use This Tool |
|----------|---------------|
| Exploring APIs manually | Interactive CLI Tester |
| Testing specific endpoints | Interactive CLI Tester |
| Debugging issues | Interactive CLI Tester |
| Seeing curl equivalents | Interactive CLI Tester |
| Quick ad-hoc testing | Interactive CLI Tester |
| Works everywhere | Interactive CLI Tester |
| CI/CD pipeline | UAT Script |
| Regression testing | UAT Script |
| Full integration test | UAT Script |
| Generating audit logs | UAT Script |

---

## Files

- `consumer_interactive_tester.py` - **Interactive CLI menu (recommended for manual testing)**
- `consumer_uat.py` - Automated sequential UAT script
- `consumer_uat_log.json` - Output log from UAT script (gitignored)
- `README.md` - This file
