/*
 * C++ WebSocket Client Example for NatsHttpGateway
 *
 * Requirements:
 *   - Boost.Beast (WebSocket support)
 *   - Boost.Asio (async I/O)
 *   - Protobuf (message parsing)
 *
 * Build:
 *   g++ -std=c++17 websocket_client_example.cpp message.pb.cc \
 *       -lprotobuf -lboost_system -pthread -o websocket_client
 *
 * Usage:
 *   ./websocket_client [ws_url]
 *   ./websocket_client ws://localhost:8080/ws/websocketmessages/events.>
 */

#include <boost/beast/core.hpp>
#include <boost/beast/websocket.hpp>
#include <boost/asio/connect.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <iostream>
#include <string>
#include <memory>
#include <chrono>
#include <iomanip>
#include "message.pb.h"

namespace beast = boost::beast;
namespace http = beast::http;
namespace websocket = beast::websocket;
namespace net = boost::asio;
using tcp = boost::asio::ip::tcp;

class WebSocketClient {
private:
    std::string host_;
    std::string port_;
    std::string path_;
    net::io_context ioc_;
    tcp::resolver resolver_;
    websocket::stream<tcp::socket> ws_;
    int message_count_;
    int max_messages_;

public:
    WebSocketClient(const std::string& host, const std::string& port, const std::string& path, int max_messages = 10)
        : host_(host)
        , port_(port)
        , path_(path)
        , resolver_(ioc_)
        , ws_(ioc_)
        , message_count_(0)
        , max_messages_(max_messages)
    {
    }

    void connect() {
        try {
            std::cout << "Connecting to ws://" << host_ << ":" << port_ << path_ << std::endl;

            // Resolve the host
            auto const results = resolver_.resolve(host_, port_);

            // Make the connection
            auto ep = net::connect(ws_.next_layer(), results);

            // Update the host string for the WebSocket handshake
            std::string host_port = host_ + ":" + std::to_string(ep.port());

            // Set WebSocket options
            ws_.set_option(websocket::stream_base::decorator(
                [](websocket::request_type& req) {
                    req.set(http::field::user_agent,
                        std::string(BOOST_BEAST_VERSION_STRING) + " websocket-client-coro");
                }));

            // Perform the WebSocket handshake
            ws_.handshake(host_port, path_);

            std::cout << "✓ WebSocket connected" << std::endl;

        } catch (std::exception const& e) {
            std::cerr << "✗ Connection error: " << e.what() << std::endl;
            throw;
        }
    }

    void stream_messages() {
        try {
            while (message_count_ < max_messages_) {
                // Read a message
                beast::flat_buffer buffer;
                ws_.read(buffer);

                // Convert buffer to string for protobuf parsing
                std::string frame_data = beast::buffers_to_string(buffer.data());

                // Parse the WebSocketFrame
                nats::messages::WebSocketFrame frame;
                if (!frame.ParseFromString(frame_data)) {
                    std::cerr << "✗ Failed to parse WebSocketFrame" << std::endl;
                    continue;
                }

                // Handle different frame types
                switch (frame.type()) {
                    case nats::messages::CONTROL:
                        handle_control_message(frame.control());
                        break;

                    case nats::messages::MESSAGE:
                        handle_stream_message(frame.message());
                        message_count_++;
                        break;

                    default:
                        std::cout << "• Unknown frame type: " << frame.type() << std::endl;
                        break;
                }
            }

            std::cout << "✓ Received " << message_count_ << " messages" << std::endl;

        } catch (beast::system_error const& se) {
            if (se.code() != websocket::error::closed) {
                std::cerr << "✗ Stream error: " << se.code().message() << std::endl;
            }
        } catch (std::exception const& e) {
            std::cerr << "✗ Stream error: " << e.what() << std::endl;
        }
    }

    void close() {
        try {
            ws_.close(websocket::close_code::normal);
            std::cout << "✓ Connection closed" << std::endl;
        } catch (std::exception const& e) {
            std::cerr << "✗ Close error: " << e.what() << std::endl;
        }
    }

private:
    void handle_control_message(const nats::messages::ControlMessage& control) {
        std::string icon;
        switch (control.type()) {
            case nats::messages::ERROR:
                icon = "✗";
                break;
            case nats::messages::SUBSCRIBE_ACK:
                icon = "✓";
                break;
            case nats::messages::CLOSE:
                icon = "✓";
                break;
            case nats::messages::KEEPALIVE:
                icon = "♥";
                break;
            default:
                icon = "•";
                break;
        }

        std::cout << icon << " Control ["
                  << nats::messages::ControlType_Name(control.type())
                  << "]: " << control.message() << std::endl;
    }

    void handle_stream_message(const nats::messages::StreamMessage& message) {
        std::cout << "  Message received:" << std::endl;
        std::cout << "    Subject:  " << message.subject() << std::endl;
        std::cout << "    Sequence: " << message.sequence() << std::endl;
        std::cout << "    Size:     " << message.size_bytes() << " bytes" << std::endl;

        if (message.has_timestamp()) {
            auto seconds = message.timestamp().seconds();
            auto nanos = message.timestamp().nanos();
            auto time = std::chrono::system_clock::from_time_t(seconds);
            auto time_t_val = std::chrono::system_clock::to_time_t(time);

            std::cout << "    Time:     "
                      << std::put_time(std::localtime(&time_t_val), "%Y-%m-%d %H:%M:%S")
                      << "." << std::setfill('0') << std::setw(3) << (nanos / 1000000)
                      << std::endl;
        }

        if (!message.consumer().empty()) {
            std::cout << "    Consumer: " << message.consumer() << std::endl;
        }

        if (!message.data().empty()) {
            // Try to display as UTF-8 string
            std::string data_str = message.data();
            if (data_str.length() > 100) {
                data_str = data_str.substr(0, 100) + "...";
            }

            // Check if printable
            bool printable = true;
            for (char c : data_str) {
                if (!isprint(static_cast<unsigned char>(c)) && !isspace(static_cast<unsigned char>(c))) {
                    printable = false;
                    break;
                }
            }

            if (printable) {
                std::cout << "    Data:     " << data_str << std::endl;
            } else {
                std::cout << "    Data:     [binary, " << message.data().length() << " bytes]" << std::endl;
            }
        }

        std::cout << std::endl;
    }
};

// Parse WebSocket URL
struct WebSocketURL {
    std::string host;
    std::string port;
    std::string path;

    static WebSocketURL parse(const std::string& url) {
        WebSocketURL result;

        // Remove ws:// or wss:// prefix
        std::string remaining = url;
        if (remaining.substr(0, 5) == "ws://") {
            remaining = remaining.substr(5);
        } else if (remaining.substr(0, 6) == "wss://") {
            remaining = remaining.substr(6);
            // Note: For wss://, you'd need to use SSL WebSocket stream
            std::cerr << "Warning: wss:// not supported in this example, treating as ws://" << std::endl;
        }

        // Find first slash (separates host:port from path)
        auto slash_pos = remaining.find('/');
        std::string host_port = remaining.substr(0, slash_pos);
        result.path = (slash_pos != std::string::npos) ? remaining.substr(slash_pos) : "/";

        // Split host and port
        auto colon_pos = host_port.find(':');
        if (colon_pos != std::string::npos) {
            result.host = host_port.substr(0, colon_pos);
            result.port = host_port.substr(colon_pos + 1);
        } else {
            result.host = host_port;
            result.port = "8080"; // Default port
        }

        return result;
    }
};

void example1_ephemeral_consumer(const std::string& base_url) {
    std::cout << "=== Example 1: Streaming from Ephemeral Consumer (events.>) ===" << std::endl;

    std::string ws_url = base_url + "/ws/websocketmessages/events.>";
    auto url = WebSocketURL::parse(ws_url);

    WebSocketClient client(url.host, url.port, url.path, 5);
    client.connect();
    client.stream_messages();
    client.close();

    std::cout << std::endl;
}

void example2_specific_subject(const std::string& base_url) {
    std::cout << "=== Example 2: Streaming from Specific Subject (events.test) ===" << std::endl;

    std::string ws_url = base_url + "/ws/websocketmessages/events.test";
    auto url = WebSocketURL::parse(ws_url);

    WebSocketClient client(url.host, url.port, url.path, 5);
    client.connect();
    client.stream_messages();
    client.close();

    std::cout << std::endl;
}

void example3_durable_consumer(const std::string& base_url) {
    std::cout << "=== Example 3: Streaming from Durable Consumer ===" << std::endl;
    std::cout << "Note: Requires pre-created consumer 'my-durable-consumer' in stream 'EVENTS'" << std::endl;
    std::cout << "Create with: nats consumer add EVENTS my-durable-consumer --filter events.> --deliver all --ack none" << std::endl;

    std::string ws_url = base_url + "/ws/websocketmessages/EVENTS/consumer/my-durable-consumer";
    auto url = WebSocketURL::parse(ws_url);

    try {
        WebSocketClient client(url.host, url.port, url.path, 5);
        client.connect();
        client.stream_messages();
        client.close();
    } catch (const std::exception& e) {
        std::cerr << "✗ Durable consumer example failed (consumer may not exist)" << std::endl;
    }

    std::cout << std::endl;
}

int main(int argc, char* argv[]) {
    // Initialize protobuf library
    GOOGLE_PROTOBUF_VERIFY_VERSION;

    // Configuration priority: CLI arg > Environment variable > Default
    std::string base_url;
    if (argc > 1) {
        base_url = argv[1];
    } else {
        const char* env_url = std::getenv("NATS_GATEWAY_URL");
        base_url = env_url ? env_url : "ws://localhost:5000";
    }

    // Remove trailing slash
    if (base_url.back() == '/') {
        base_url.pop_back();
    }

    std::cout << "C++ WebSocket Client Example - Connecting to " << base_url << std::endl;
    std::cout << std::string(80, '=') << std::endl;
    std::cout << std::endl;

    try {
        // Example 1: Ephemeral consumer with wildcard
        example1_ephemeral_consumer(base_url);

        // Example 2: Specific subject
        example2_specific_subject(base_url);

        // Example 3: Durable consumer (commented out by default)
        // example3_durable_consumer(base_url);

        std::cout << std::string(80, '=') << std::endl;
        std::cout << "✓ All examples completed successfully!" << std::endl;

    } catch (std::exception const& e) {
        std::cerr << "✗ Error: " << e.what() << std::endl;
        std::cerr << "  Make sure NatsHttpGateway is running at " << base_url << std::endl;
        std::cerr << "  Make sure NATS is running and has messages to stream" << std::endl;
        return 1;
    }

    // Shutdown protobuf library
    google::protobuf::ShutdownProtobufLibrary();

    return 0;
}
