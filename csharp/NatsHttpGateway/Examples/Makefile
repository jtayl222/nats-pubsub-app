# Makefile for C++ Examples
#
# Prerequisites:
#   - protobuf compiler (protoc)
#   - boost libraries (system, beast, asio)
#   - C++17 compiler (g++ or clang++)

CXX = g++
CXXFLAGS = -std=c++17 -Wall -O2
INCLUDES = -I.
LIBS = -lprotobuf -lboost_system -pthread

# Protobuf files
PROTO_DIR = ../Protos
PROTO_FILE = $(PROTO_DIR)/message.proto
PROTO_SRC = message.pb.cc
PROTO_HDR = message.pb.h

# Targets
HTTP_CLIENT = http_client
WEBSOCKET_CLIENT = websocket_client

.PHONY: all clean protobuf

all: protobuf $(HTTP_CLIENT) $(WEBSOCKET_CLIENT)

# Generate protobuf sources
protobuf: $(PROTO_SRC)

$(PROTO_SRC) $(PROTO_HDR): $(PROTO_FILE)
	@echo "Generating protobuf sources..."
	protoc --cpp_out=. --proto_path=$(PROTO_DIR) $(PROTO_FILE)
	@echo "✓ Generated $(PROTO_SRC) and $(PROTO_HDR)"

# Build HTTP client
$(HTTP_CLIENT): http_client_example.cpp $(PROTO_SRC)
	@echo "Building HTTP client..."
	$(CXX) $(CXXFLAGS) $(INCLUDES) -o $@ $^ -lprotobuf -lcurl
	@echo "✓ Built $(HTTP_CLIENT)"

# Build WebSocket client
$(WEBSOCKET_CLIENT): websocket_client_example.cpp $(PROTO_SRC)
	@echo "Building WebSocket client..."
	$(CXX) $(CXXFLAGS) $(INCLUDES) -o $@ $^ $(LIBS)
	@echo "✓ Built $(WEBSOCKET_CLIENT)"

# Clean build artifacts
clean:
	@echo "Cleaning build artifacts..."
	rm -f $(HTTP_CLIENT) $(WEBSOCKET_CLIENT)
	rm -f $(PROTO_SRC) $(PROTO_HDR)
	rm -f *.o
	@echo "✓ Cleaned"

# Help
help:
	@echo "C++ Examples Makefile"
	@echo ""
	@echo "Targets:"
	@echo "  all              - Build all examples (default)"
	@echo "  protobuf         - Generate protobuf sources only"
	@echo "  http_client      - Build HTTP/REST client example"
	@echo "  websocket_client - Build WebSocket client example"
	@echo "  clean            - Remove build artifacts"
	@echo "  help             - Show this help message"
	@echo ""
	@echo "Usage:"
	@echo "  make                    # Build everything"
	@echo "  make clean              # Clean"
	@echo "  make http_client        # Build HTTP client only"
	@echo "  make websocket_client   # Build WebSocket client only"
	@echo ""
	@echo "Run:"
	@echo "  ./http_client [base_url]"
	@echo "  ./http_client http://localhost:8080"
	@echo "  ./websocket_client [ws_url]"
	@echo "  ./websocket_client ws://localhost:8080"
