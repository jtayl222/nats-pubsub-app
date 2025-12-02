# C++ Examples Guide

Complete guide for using the C++ HTTP/REST and WebSocket clients with NatsHttpGateway.

## Prerequisites

- C++17 compatible compiler (GCC 7+, Clang 5+, MSVC 2017+)
- Protocol Buffers compiler (`protoc`)
- libcurl (for HTTP client)
- Boost libraries (for WebSocket client: Beast, Asio, System)
- CMake 3.15+ or Make
- NATS server running (`nats://localhost:4222`)
- NatsHttpGateway running (`http://localhost:8080`)

## Quick Start

```bash
# Navigate to Examples directory
cd Examples

# Install dependencies (macOS)
brew install protobuf boost curl cmake

# Build all examples
make

# Run HTTP client
./http_client http://localhost:8080

# Run WebSocket client
./websocket_client ws://localhost:8080/ws/websocketmessages/events.>
```

## Installing Dependencies

### macOS (Homebrew)

```bash
brew install protobuf boost curl cmake
```

### Ubuntu/Debian

```bash
sudo apt-get update
sudo apt-get install -y \
    build-essential cmake \
    protobuf-compiler libprotobuf-dev \
    libboost-all-dev \
    libcurl4-openssl-dev
```

### Fedora/RHEL/CentOS

```bash
sudo dnf install -y \
    gcc-c++ cmake \
    protobuf-devel protobuf-compiler \
    boost-devel \
    libcurl-devel
```

### Verify Installation

```bash
# Check compiler
g++ --version      # or clang++ --version

# Check protobuf
protoc --version

# Check boost
ls /usr/local/include/boost  # or /usr/include/boost

# Check curl
curl-config --version
```

## Building the Examples

### Option 1: Using Make (Recommended)

```bash
# Navigate to Examples directory
cd /path/to/NatsHttpGateway/Examples

# Build all examples
make

# Or build individually
make http_client       # HTTP client only
make websocket_client  # WebSocket client only

# Generate protobuf sources only
make protobuf

# Clean build artifacts
make clean

# Show help
make help
```

### Option 2: Using CMake

```bash
# Navigate to Examples directory
cd /path/to/NatsHttpGateway/Examples

# Create build directory
mkdir build && cd build

# Configure
cmake ..

# Build
make

# Executables will be in build/ directory
./http_client
./websocket_client

# Clean
cd .. && rm -rf build
```

### Option 3: Manual Build

```bash
# Generate protobuf sources
protoc --proto_path=../Protos --cpp_out=. message.proto

# Build HTTP client
g++ -std=c++17 -o http_client \
    http_client_example.cpp message.pb.cc \
    -lprotobuf -lcurl

# Build WebSocket client
g++ -std=c++17 -o websocket_client \
    websocket_client_example.cpp message.pb.cc \
    -lprotobuf -lboost_system -pthread

# On some systems, you may need to specify include/library paths:
g++ -std=c++17 -o websocket_client \
    websocket_client_example.cpp message.pb.cc \
    -I/usr/local/include \
    -L/usr/local/lib \
    -lprotobuf -lboost_system -pthread
```

## Running the Examples

### HTTP/REST Client

**File:** `http_client_example.cpp`

```bash
# Build first
make http_client

# Run with default settings (localhost:8080)
./http_client

# Or specify custom URL
./http_client http://gateway:8080
```

**What it does:**
1. Publishes a generic protobuf message to `events.test`
2. Publishes a `UserEvent` to `events.user.created`
3. Publishes a `PaymentEvent` to `payments.credit_card.approved`
4. Fetches and displays recent messages

**Example output:**
```
=== Example 1: Publishing Generic Message ===
Protobuf payload size: 142 bytes
✓ Published successfully!
  Stream:   EVENTS
  Sequence: 42
  Subject:  events.test

=== Example 2: Publishing UserEvent ===
✓ UserEvent published!
  User ID: user-3425
  Event Type: created
  Stream: EVENTS, Sequence: 43
```

### WebSocket Client

**File:** `websocket_client_example.cpp`

```bash
# Build first
make websocket_client

# Run with default settings (localhost:8080)
./websocket_client ws://localhost:8080

# Stream from specific subject filter
./websocket_client ws://localhost:8080/ws/websocketmessages/events.>
./websocket_client ws://localhost:8080/ws/websocketmessages/events.test

# Stream from durable consumer (requires pre-created consumer)
./websocket_client ws://localhost:8080/ws/websocketmessages/EVENTS/consumer/my-durable-consumer
```

**What it does:**
1. Connects to the WebSocket endpoint
2. Receives and parses protobuf binary frames
3. Handles both control and data messages
4. Displays message details in real-time

**Example output:**
```
=== Example 1: Streaming from Ephemeral Consumer (events.>) ===
Connecting to: ws://localhost:8080/ws/websocketmessages/events.>
✓ WebSocket connected
✓ Control [SUBSCRIBE_ACK]: Subscribed to events.>
  Message received:
    Subject:  events.test
    Sequence: 45
    Size:     156 bytes
    Time:     2025-12-01 15:30:42.123
    Data:     {"message": "Hello from C++!"}

✓ Received 5 messages
```

### Durable Consumer Example

The durable consumer example is commented out in code. To use it:

1. **Create a durable consumer:**
   ```bash
   nats consumer add EVENTS my-durable-consumer \
     --filter events.> \
     --deliver all \
     --ack none
   ```

2. **Uncomment in code:**
   ```cpp
   // In websocket_client_example.cpp main(), uncomment:
   example3_durable_consumer(base_url);
   ```

3. **Rebuild and run:**
   ```bash
   make websocket_client
   ./websocket_client ws://localhost:8080
   ```

## Code Examples

### Publishing a Message

```cpp
#include "message.pb.h"
#include <curl/curl.h>

nats::messages::PublishMessage message;
message.set_message_id("msg-123");
message.set_subject("events.test");
message.set_source("cpp-app");
message.set_data("{\"key\": \"value\"}");
(*message.mutable_metadata())["client"] = "cpp";

// Serialize
std::string request_body;
message.SerializeToString(&request_body);

// Send via HTTP
CURL* curl = curl_easy_init();
curl_easy_setopt(curl, CURLOPT_URL, "http://localhost:8080/api/proto/ProtobufMessages/events.test");
curl_easy_setopt(curl, CURLOPT_POSTFIELDS, request_body.c_str());
// ... (see http_client_example.cpp for full implementation)
```

### Streaming Messages

```cpp
#include <boost/beast/core.hpp>
#include <boost/beast/websocket.hpp>
#include "message.pb.h"

namespace websocket = boost::beast::websocket;
namespace net = boost::asio;

net::io_context ioc;
websocket::stream<tcp::socket> ws(ioc);

// Connect
// ... (connection code)

// Read and parse frame
beast::flat_buffer buffer;
ws.read(buffer);

std::string frame_data = beast::buffers_to_string(buffer.data());
nats::messages::WebSocketFrame frame;
frame.ParseFromString(frame_data);

if (frame.type() == nats::messages::MESSAGE) {
    std::cout << "Subject: " << frame.message().subject() << std::endl;
    std::cout << "Data: " << frame.message().data() << std::endl;
}
```

## Troubleshooting

### Build errors - protobuf/boost/curl not found

**Problem:** CMake or compiler can't find dependencies

**Solution:**
```bash
# macOS
brew install protobuf boost curl

# Ubuntu/Debian
sudo apt-get install protobuf-compiler libprotobuf-dev \
    libboost-all-dev libcurl4-openssl-dev

# Verify installations
protoc --version
pkg-config --modversion protobuf
ls /usr/local/include/boost
```

### Undefined reference to boost::beast or boost::asio

**Problem:** Linker can't find Boost libraries

**Solution:**
```bash
# Make sure you're linking Boost System library
g++ ... -lboost_system -pthread

# On some systems, you may need to specify the library path
g++ ... -L/usr/local/lib -lboost_system -pthread

# Check where Boost libraries are installed
find /usr -name "libboost_system*"
```

### message.pb.h: No such file or directory

**Problem:** Protobuf sources not generated

**Solution:**
```bash
# Generate protobuf sources
make protobuf

# Or manually
protoc --proto_path=../Protos --cpp_out=. message.proto

# Verify
ls message.pb.h message.pb.cc
```

### error while loading shared libraries: libprotobuf.so.XX

**Problem:** Runtime linker can't find shared libraries

**Solution:**
```bash
# Check library paths
ldd ./websocket_client
ldd ./http_client

# Add library path if needed
export LD_LIBRARY_PATH=/usr/local/lib:$LD_LIBRARY_PATH

# Or add to ~/.bashrc for permanent fix
echo 'export LD_LIBRARY_PATH=/usr/local/lib:$LD_LIBRARY_PATH' >> ~/.bashrc
source ~/.bashrc

# On macOS, use DYLD_LIBRARY_PATH
export DYLD_LIBRARY_PATH=/usr/local/lib:$DYLD_LIBRARY_PATH
```

### fatal error: curl/curl.h: No such file or directory

**Problem:** libcurl development headers not installed

**Solution:**
```bash
# Ubuntu/Debian
sudo apt-get install libcurl4-openssl-dev

# Fedora/RHEL
sudo dnf install libcurl-devel

# macOS
brew install curl
```

### WebSocket handshake failed

**Problem:** Can't connect to WebSocket endpoint

**Solutions:**
1. Check gateway is running: `curl http://localhost:8080/health`
2. Verify WebSocket URL format: `ws://host:port/path` (not `http://`)
3. Check for firewall blocking WebSocket connections
4. Verify endpoint exists: `curl http://localhost:8080/`

### No messages received

**Problem:** Connected but not receiving messages

**Solutions:**
1. WebSocket streams **new** messages only
2. Connect WebSocket client first, then publish:
   ```bash
   # Terminal 1
   ./websocket_client ws://localhost:8080/ws/websocketmessages/events.> &

   # Terminal 2
   ./http_client
   ```
3. For historical messages, use durable consumer with `DeliverPolicy.All`

## Workflow Examples

### End-to-End Test

```bash
# Terminal 1: Start gateway
cd /path/to/NatsHttpGateway
dotnet run

# Terminal 2: Build and start WebSocket subscriber
cd Examples
make websocket_client
./websocket_client ws://localhost:8080/ws/websocketmessages/events.> &

# Terminal 3: Build and publish messages
make http_client
./http_client

# You'll see messages in Terminal 2 in real-time
```

### Continuous Streaming

Modify `websocket_client_example.cpp` to run indefinitely:

```cpp
// Remove message count limit
while (true) {  // Instead of: while (message_count_ < max_messages_)
    beast::flat_buffer buffer;
    ws_.read(buffer);
    // ...
}
```

### Integration with Your Project

1. **Copy source files:**
   ```bash
   cp http_client_example.cpp /path/to/your/project/
   cp websocket_client_example.cpp /path/to/your/project/
   ```

2. **Generate protobuf sources in your project:**
   ```bash
   protoc --proto_path=/path/to/Protos --cpp_out=. message.proto
   ```

3. **Update your CMakeLists.txt:**
   ```cmake
   find_package(Protobuf REQUIRED)
   find_package(Boost REQUIRED COMPONENTS system)
   find_package(CURL REQUIRED)

   add_executable(my_client my_client.cpp message.pb.cc)
   target_link_libraries(my_client
       ${Protobuf_LIBRARIES}
       ${Boost_LIBRARIES}
       ${CURL_LIBRARIES}
       pthread
   )
   ```

## Performance Tips

1. **Reuse connections:** Create one HTTP client instance for multiple requests
2. **Connection pooling:** Use CURL multi interface for concurrent requests
3. **Binary protobuf:** Already optimal, but avoid unnecessary serialization/deserialization
4. **Async I/O:** Boost.Asio is async by default, leverage it for performance
5. **Buffer sizes:** Adjust WebSocket buffer size based on message sizes

## Additional Resources

- [Boost.Beast documentation](https://www.boost.org/doc/libs/release/libs/beast/)
- [Boost.Asio documentation](https://www.boost.org/doc/libs/release/libs/asio/)
- [libcurl documentation](https://curl.se/libcurl/)
- [Protocol Buffers C++ Tutorial](https://protobuf.dev/getting-started/cpptutorial/)
- [Main Examples README](README.md)

## Next Steps

- Adapt examples for your use case
- Add error handling for production use
- Implement reconnection logic for WebSocket
- Add authentication headers if needed
- Consider using SSL/TLS for secure connections
