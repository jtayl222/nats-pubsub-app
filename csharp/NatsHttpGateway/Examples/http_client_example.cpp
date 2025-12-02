/*
 * C++ HTTP Client Example for NatsHttpGateway
 *
 * Requirements:
 *   - libcurl (HTTP client)
 *   - Protobuf (message parsing)
 *
 * Build:
 *   g++ -std=c++17 http_client_example.cpp message.pb.cc \
 *       -lprotobuf -lcurl -o http_client
 *
 * Usage:
 *   ./http_client [base_url]
 *   ./http_client http://localhost:8080
 */

#include <curl/curl.h>
#include <iostream>
#include <string>
#include <vector>
#include <memory>
#include <ctime>
#include <iomanip>
#include "message.pb.h"

// Callback for writing HTTP response data
static size_t write_callback(void* contents, size_t size, size_t nmemb, void* userp) {
    ((std::string*)userp)->append((char*)contents, size * nmemb);
    return size * nmemb;
}

class HttpClient {
private:
    std::string base_url_;
    CURL* curl_;

public:
    HttpClient(const std::string& base_url) : base_url_(base_url) {
        curl_global_init(CURL_GLOBAL_DEFAULT);
        curl_ = curl_easy_init();
        if (!curl_) {
            throw std::runtime_error("Failed to initialize CURL");
        }
    }

    ~HttpClient() {
        if (curl_) {
            curl_easy_cleanup(curl_);
        }
        curl_global_cleanup();
    }

    // Publish a message to NATS via HTTP
    bool publish_message(const std::string& subject, const nats::messages::PublishMessage& message) {
        std::string url = base_url_ + "/api/proto/ProtobufMessages/" + subject;
        std::string response_data;

        // Serialize the message to protobuf
        std::string request_body;
        if (!message.SerializeToString(&request_body)) {
            std::cerr << "✗ Failed to serialize message" << std::endl;
            return false;
        }

        // Set up the request
        curl_easy_reset(curl_);
        curl_easy_setopt(curl_, CURLOPT_URL, url.c_str());
        curl_easy_setopt(curl_, CURLOPT_POST, 1L);
        curl_easy_setopt(curl_, CURLOPT_POSTFIELDS, request_body.c_str());
        curl_easy_setopt(curl_, CURLOPT_POSTFIELDSIZE, request_body.size());

        // Set headers
        struct curl_slist* headers = nullptr;
        headers = curl_slist_append(headers, "Content-Type: application/x-protobuf");
        curl_easy_setopt(curl_, CURLOPT_HTTPHEADER, headers);

        // Set response callback
        curl_easy_setopt(curl_, CURLOPT_WRITEFUNCTION, write_callback);
        curl_easy_setopt(curl_, CURLOPT_WRITEDATA, &response_data);

        // Perform the request
        CURLcode res = curl_easy_perform(curl_);
        curl_slist_free_all(headers);

        if (res != CURLE_OK) {
            std::cerr << "✗ HTTP request failed: " << curl_easy_strerror(res) << std::endl;
            return false;
        }

        // Check response code
        long response_code;
        curl_easy_getinfo(curl_, CURLINFO_RESPONSE_CODE, &response_code);
        if (response_code != 200) {
            std::cerr << "✗ Server returned status: " << response_code << std::endl;
            return false;
        }

        // Parse the response
        nats::messages::PublishAck ack;
        if (!ack.ParseFromString(response_data)) {
            std::cerr << "✗ Failed to parse response" << std::endl;
            return false;
        }

        std::cout << "✓ Published successfully!" << std::endl;
        std::cout << "  Stream:   " << ack.stream() << std::endl;
        std::cout << "  Sequence: " << ack.sequence() << std::endl;
        std::cout << "  Subject:  " << ack.subject() << std::endl;

        return true;
    }

    // Fetch messages from NATS via HTTP
    bool fetch_messages(const std::string& subject, int limit = 10) {
        std::string url = base_url_ + "/api/proto/ProtobufMessages/" + subject + "?limit=" + std::to_string(limit);
        std::string response_data;

        // Set up the request
        curl_easy_reset(curl_);
        curl_easy_setopt(curl_, CURLOPT_URL, url.c_str());
        curl_easy_setopt(curl_, CURLOPT_HTTPGET, 1L);

        // Set headers
        struct curl_slist* headers = nullptr;
        headers = curl_slist_append(headers, "Accept: application/x-protobuf");
        curl_easy_setopt(curl_, CURLOPT_HTTPHEADER, headers);

        // Set response callback
        curl_easy_setopt(curl_, CURLOPT_WRITEFUNCTION, write_callback);
        curl_easy_setopt(curl_, CURLOPT_WRITEDATA, &response_data);

        // Perform the request
        CURLcode res = curl_easy_perform(curl_);
        curl_slist_free_all(headers);

        if (res != CURLE_OK) {
            std::cerr << "✗ HTTP request failed: " << curl_easy_strerror(res) << std::endl;
            return false;
        }

        // Check response code
        long response_code;
        curl_easy_getinfo(curl_, CURLINFO_RESPONSE_CODE, &response_code);
        if (response_code != 200) {
            std::cerr << "✗ Server returned status: " << response_code << std::endl;
            return false;
        }

        // Parse the response
        nats::messages::FetchResponse fetch_response;
        if (!fetch_response.ParseFromString(response_data)) {
            std::cerr << "✗ Failed to parse response" << std::endl;
            return false;
        }

        std::cout << "✓ Fetched " << fetch_response.count() << " messages from " << fetch_response.stream() << std::endl;
        std::cout << "  Subject: " << fetch_response.subject() << std::endl;
        std::cout << "  Messages:" << std::endl;

        for (const auto& msg : fetch_response.messages()) {
            std::cout << "    [" << msg.sequence() << "] " << msg.subject() << std::endl;
            std::cout << "        Size: " << msg.size_bytes() << " bytes" << std::endl;

            if (msg.has_timestamp()) {
                auto seconds = msg.timestamp().seconds();
                auto time_t_val = static_cast<time_t>(seconds);
                std::cout << "        Time: "
                          << std::put_time(std::localtime(&time_t_val), "%Y-%m-%d %H:%M:%S")
                          << std::endl;
            }

            // Try to display data
            if (!msg.data().empty()) {
                std::string data_str = msg.data();
                if (data_str.length() > 50) {
                    data_str = data_str.substr(0, 50) + "...";
                }

                bool printable = true;
                for (char c : data_str) {
                    if (!isprint(static_cast<unsigned char>(c)) && !isspace(static_cast<unsigned char>(c))) {
                        printable = false;
                        break;
                    }
                }

                if (printable) {
                    std::cout << "        Data: " << data_str << std::endl;
                } else {
                    std::cout << "        Data: [binary, " << msg.data().length() << " bytes]" << std::endl;
                }
            }
        }

        return true;
    }
};

// Helper to generate UUID (simple version)
std::string generate_uuid() {
    static bool initialized = false;
    if (!initialized) {
        srand(time(nullptr));
        initialized = true;
    }

    char uuid[37];
    snprintf(uuid, sizeof(uuid),
             "%08x-%04x-%04x-%04x-%012x",
             rand(), rand() & 0xFFFF, rand() & 0xFFFF,
             rand() & 0xFFFF, rand());
    return std::string(uuid);
}

void example1_publish_generic_message(HttpClient& client) {
    std::cout << "=== Example 1: Publishing Generic Message ===" << std::endl;

    nats::messages::PublishMessage message;
    message.set_message_id(generate_uuid());
    message.set_subject("events.test");
    message.set_source("cpp-client");

    // Set timestamp
    auto timestamp = message.mutable_timestamp();
    auto now = std::chrono::system_clock::now();
    auto seconds = std::chrono::duration_cast<std::chrono::seconds>(now.time_since_epoch());
    timestamp->set_seconds(seconds.count());

    // Set data
    message.set_data(R"({"message": "Hello from C++!"})");

    // Set metadata
    (*message.mutable_metadata())["client"] = "cpp";
    (*message.mutable_metadata())["version"] = "1.0";

    std::cout << "Protobuf payload size: " << message.ByteSizeLong() << " bytes" << std::endl;

    client.publish_message("events.test", message);
    std::cout << std::endl;
}

void example2_publish_user_event(HttpClient& client) {
    std::cout << "=== Example 2: Publishing UserEvent ===" << std::endl;

    nats::messages::UserEvent user_event;
    user_event.set_user_id("user-" + std::to_string(rand() % 9000 + 1000));
    user_event.set_event_type("created");
    user_event.set_email("cppuser@example.com");

    auto timestamp = user_event.mutable_occurred_at();
    auto now = std::chrono::system_clock::now();
    auto seconds = std::chrono::duration_cast<std::chrono::seconds>(now.time_since_epoch());
    timestamp->set_seconds(seconds.count());

    (*user_event.mutable_attributes())["plan"] = "premium";
    (*user_event.mutable_attributes())["language"] = "cpp";

    // Wrap in PublishMessage
    nats::messages::PublishMessage message;
    message.set_message_id(generate_uuid());
    message.set_subject("events.user.created");
    message.set_source("cpp-client");

    std::string user_event_data;
    user_event.SerializeToString(&user_event_data);
    message.set_data(user_event_data);

    client.publish_message("events.user.created/user-event", message);
    std::cout << std::endl;
}

void example3_publish_payment_event(HttpClient& client) {
    std::cout << "=== Example 3: Publishing PaymentEvent ===" << std::endl;

    nats::messages::PaymentEvent payment_event;
    payment_event.set_transaction_id("txn-" + generate_uuid());
    payment_event.set_status("approved");
    payment_event.set_amount(149.99);
    payment_event.set_currency("USD");
    payment_event.set_card_last_four("5678");

    auto timestamp = payment_event.mutable_processed_at();
    auto now = std::chrono::system_clock::now();
    auto seconds = std::chrono::duration_cast<std::chrono::seconds>(now.time_since_epoch());
    timestamp->set_seconds(seconds.count());

    // Wrap in PublishMessage
    nats::messages::PublishMessage message;
    message.set_message_id(generate_uuid());
    message.set_subject("payments.credit_card.approved");
    message.set_source("cpp-client");

    std::string payment_event_data;
    payment_event.SerializeToString(&payment_event_data);
    message.set_data(payment_event_data);

    std::cout << "✓ PaymentEvent published!" << std::endl;
    std::cout << "  Transaction ID: " << payment_event.transaction_id() << std::endl;
    std::cout << "  Amount: $" << payment_event.amount() << " " << payment_event.currency() << std::endl;
    std::cout << "  Status: " << payment_event.status() << std::endl;

    client.publish_message("payments.credit_card.approved/payment-event", message);
    std::cout << std::endl;
}

void example4_fetch_messages(HttpClient& client, const std::string& subject, int limit) {
    std::cout << "=== Example 4: Fetching Messages (" << subject << ") ===" << std::endl;
    client.fetch_messages(subject, limit);
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
        base_url = env_url ? env_url : "http://localhost:5000";
    }

    // Remove trailing slash
    if (base_url.back() == '/') {
        base_url.pop_back();
    }

    std::cout << "C++ HTTP Client Example - Connecting to " << base_url << std::endl;
    std::cout << std::string(60, '=') << std::endl;
    std::cout << std::endl;

    try {
        HttpClient client(base_url);

        example1_publish_generic_message(client);
        example2_publish_user_event(client);
        example3_publish_payment_event(client);
        example4_fetch_messages(client, "events.test", 5);
        example4_fetch_messages(client, "events.user.created", 3);

        std::cout << std::string(60, '=') << std::endl;
        std::cout << "✓ All examples completed successfully!" << std::endl;

    } catch (std::exception const& e) {
        std::cerr << "✗ Error: " << e.what() << std::endl;
        std::cerr << "  Make sure NatsHttpGateway is running at " << base_url << std::endl;
        return 1;
    }

    // Shutdown protobuf library
    google::protobuf::ShutdownProtobufLibrary();

    return 0;
}
