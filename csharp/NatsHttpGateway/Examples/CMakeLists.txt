cmake_minimum_required(VERSION 3.15)
project(NatsHttpGatewayExamples CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find required packages
find_package(Protobuf REQUIRED)
find_package(Boost REQUIRED COMPONENTS system)
find_package(CURL REQUIRED)

# Generate protobuf sources
set(PROTO_FILES "${CMAKE_CURRENT_SOURCE_DIR}/../Protos/message.proto")
protobuf_generate_cpp(PROTO_SRCS PROTO_HDRS ${PROTO_FILES})

# Include directories
include_directories(
    ${CMAKE_CURRENT_BINARY_DIR}
    ${Protobuf_INCLUDE_DIRS}
    ${Boost_INCLUDE_DIRS}
    ${CURL_INCLUDE_DIRS}
)

# HTTP client example
add_executable(http_client
    http_client_example.cpp
    ${PROTO_SRCS}
)

target_link_libraries(http_client
    ${Protobuf_LIBRARIES}
    ${CURL_LIBRARIES}
)

# WebSocket client example
add_executable(websocket_client
    websocket_client_example.cpp
    ${PROTO_SRCS}
)

target_link_libraries(websocket_client
    ${Protobuf_LIBRARIES}
    ${Boost_LIBRARIES}
    pthread
)

# Install targets
install(TARGETS http_client websocket_client
    RUNTIME DESTINATION bin
)

# Print build info
message(STATUS "Protobuf version: ${Protobuf_VERSION}")
message(STATUS "Boost version: ${Boost_VERSION}")
message(STATUS "Generated protobuf files: ${PROTO_SRCS}")
