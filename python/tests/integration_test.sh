#!/bin/bash
set -euo pipefail

# integration_test.sh - End-to-end integration test for NATS pubsub app
# Usage: ./integration_test.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

TESTS_PASSED=0
TESTS_FAILED=0

log_info() {
    echo -e "${BLUE}[TEST]${NC} $1"
}

log_pass() {
    echo -e "${GREEN}[PASS]${NC} $1"
    ((TESTS_PASSED++))
}

log_fail() {
    echo -e "${RED}[FAIL]${NC} $1"
    ((TESTS_FAILED++))
}

test_containers_running() {
    log_info "Test: All containers are running"

    local nats_running=$(docker ps --filter "name=nats-server" --filter "status=running" --format '{{.Names}}' | grep -c "^nats-server$" || echo "0")
    local pub_running=$(docker ps --filter "name=nats-publisher" --filter "status=running" --format '{{.Names}}' | grep -c "^nats-publisher$" || echo "0")
    local sub_running=$(docker ps --filter "name=nats-subscriber" --filter "status=running" --format '{{.Names}}' | grep -c "^nats-subscriber$" || echo "0")

    if [ "$nats_running" = "1" ] && [ "$pub_running" = "1" ] && [ "$sub_running" = "1" ]; then
        log_pass "All 3 containers running"
        return 0
    else
        log_fail "Containers not running (nats: $nats_running, pub: $pub_running, sub: $sub_running)"
        return 1
    fi
}

test_nats_health() {
    log_info "Test: NATS server is healthy"

    local response=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8222/healthz" || echo "000")

    if [ "$response" = "200" ]; then
        log_pass "NATS health check returns 200"
        return 0
    else
        log_fail "NATS health check returned $response"
        return 1
    fi
}

test_nats_monitoring() {
    log_info "Test: NATS monitoring endpoints"

    local varz=$(curl -s "http://localhost:8222/varz" || echo "{}")
    local server_name=$(echo "$varz" | jq -r '.server_name' 2>/dev/null || echo "")

    if [ -n "$server_name" ]; then
        log_pass "NATS monitoring API working (server: $server_name)"
        return 0
    else
        log_fail "NATS monitoring API not responding"
        return 1
    fi
}

test_client_connections() {
    log_info "Test: Publisher and subscriber connected"

    local connz=$(curl -s "http://localhost:8222/connz" || echo "{}")
    local num_connections=$(echo "$connz" | jq -r '.num_connections' 2>/dev/null || echo "0")

    if [ "$num_connections" -ge 2 ]; then
        log_pass "NATS has $num_connections client connections"
        return 0
    else
        log_fail "NATS only has $num_connections connections (expected >= 2)"
        return 1
    fi
}

test_subscriptions() {
    log_info "Test: Subscriber has active subscription"

    local subsz=$(curl -s "http://localhost:8222/subsz" || echo "{}")
    local num_subs=$(echo "$subsz" | jq -r '.num_subscriptions' 2>/dev/null || echo "0")

    if [ "$num_subs" -ge 1 ]; then
        log_pass "NATS has $num_subs active subscription(s)"
        return 0
    else
        log_fail "No active subscriptions found"
        return 1
    fi
}

test_publisher_publishing() {
    log_info "Test: Publisher is publishing messages"

    sleep 5  # Wait for some messages

    local logs=$(docker logs --tail 10 nats-publisher 2>&1 || echo "")
    local published_count=$(echo "$logs" | grep -c "Message published" || echo "0")

    if [ "$published_count" -gt 0 ]; then
        log_pass "Publisher has published $published_count message(s) in recent logs"
        return 0
    else
        log_fail "No published messages found in recent logs"
        return 1
    fi
}

test_subscriber_receiving() {
    log_info "Test: Subscriber is receiving messages"

    local logs=$(docker logs --tail 10 nats-subscriber 2>&1 || echo "")
    local received_count=$(echo "$logs" | grep -c "Message received" || echo "0")

    if [ "$received_count" -gt 0 ]; then
        log_pass "Subscriber has received $received_count message(s) in recent logs"
        return 0
    else
        log_fail "No received messages found in recent logs"
        return 1
    fi
}

test_message_flow_rate() {
    log_info "Test: Message flow rate"

    # Count messages before
    local pub_before=$(docker logs nats-publisher 2>&1 | grep -c "Message published" || echo "0")
    local sub_before=$(docker logs nats-subscriber 2>&1 | grep -c "Message received" || echo "0")

    # Wait 10 seconds
    sleep 10

    # Count messages after
    local pub_after=$(docker logs nats-publisher 2>&1 | grep -c "Message published" || echo "0")
    local sub_after=$(docker logs nats-subscriber 2>&1 | grep -c "Message received" || echo "0")

    local pub_rate=$((pub_after - pub_before))
    local sub_rate=$((sub_after - sub_before))

    if [ "$pub_rate" -ge 3 ] && [ "$sub_rate" -ge 3 ]; then
        log_pass "Message flow rate: $pub_rate published, $sub_rate received in 10s"
        return 0
    else
        log_fail "Low message flow: $pub_rate published, $sub_rate received in 10s (expected >= 3)"
        return 1
    fi
}

test_json_log_format() {
    log_info "Test: Logs are in JSON format"

    local pub_log=$(docker logs --tail 1 nats-publisher 2>&1)
    local sub_log=$(docker logs --tail 1 nats-subscriber 2>&1)

    local pub_valid=false
    local sub_valid=false

    if echo "$pub_log" | jq . > /dev/null 2>&1; then
        pub_valid=true
    fi

    if echo "$sub_log" | jq . > /dev/null 2>&1; then
        sub_valid=true
    fi

    if $pub_valid && $sub_valid; then
        log_pass "Both publisher and subscriber use JSON logging"
        return 0
    else
        log_fail "JSON logging validation failed (pub: $pub_valid, sub: $sub_valid)"
        return 1
    fi
}

test_log_fields() {
    log_info "Test: Log fields are complete"

    local pub_log=$(docker logs --tail 5 nats-publisher 2>&1 | grep "Message published" | head -1)

    if [ -z "$pub_log" ]; then
        log_fail "No publisher logs found"
        return 1
    fi

    local has_timestamp=$(echo "$pub_log" | jq -e '.timestamp' > /dev/null 2>&1 && echo "true" || echo "false")
    local has_level=$(echo "$pub_log" | jq -e '.level' > /dev/null 2>&1 && echo "true" || echo "false")
    local has_message=$(echo "$pub_log" | jq -e '.message' > /dev/null 2>&1 && echo "true" || echo "false")
    local has_message_id=$(echo "$pub_log" | jq -e '.message_id' > /dev/null 2>&1 && echo "true" || echo "false")

    if [ "$has_timestamp" = "true" ] && [ "$has_level" = "true" ] && [ "$has_message" = "true" ]; then
        log_pass "Log entries contain required fields (timestamp, level, message)"
        return 0
    else
        log_fail "Log entries missing required fields"
        return 1
    fi
}

test_metrics_logging() {
    log_info "Test: Metrics are being logged"

    local pub_logs=$(docker logs nats-publisher 2>&1 || echo "")
    local has_metrics=$(echo "$pub_logs" | grep -c "Publisher metrics" || echo "0")

    if [ "$has_metrics" -gt 0 ]; then
        log_pass "Publisher is logging metrics"
        return 0
    else
        log_fail "No metrics found in publisher logs"
        return 1
    fi
}

test_latency_tracking() {
    log_info "Test: Latency is being tracked"

    local sub_logs=$(docker logs --tail 20 nats-subscriber 2>&1 || echo "")
    local has_latency=$(echo "$sub_logs" | grep "Message received" | grep -c "latency_ms" || echo "0")

    if [ "$has_latency" -gt 0 ]; then
        log_pass "Subscriber is tracking message latency"
        return 0
    else
        log_fail "No latency tracking found in subscriber logs"
        return 1
    fi
}

test_docker_labels() {
    log_info "Test: Docker labels for logging"

    local pub_labels=$(docker inspect nats-publisher --format='{{index .Config.Labels "logging"}}' 2>/dev/null || echo "")
    local sub_labels=$(docker inspect nats-subscriber --format='{{index .Config.Labels "logging"}}' 2>/dev/null || echo "")

    if [ "$pub_labels" = "enabled" ] && [ "$sub_labels" = "enabled" ]; then
        log_pass "Containers have logging labels configured"
        return 0
    else
        log_fail "Logging labels not properly configured"
        return 1
    fi
}

print_summary() {
    local total=$((TESTS_PASSED + TESTS_FAILED))

    echo ""
    echo "========================================"
    echo "Integration Test Summary"
    echo "========================================"
    echo "Total tests: $total"
    echo -e "Passed: ${GREEN}${TESTS_PASSED}${NC}"
    echo -e "Failed: ${RED}${TESTS_FAILED}${NC}"
    echo "========================================"

    if [ $TESTS_FAILED -eq 0 ]; then
        echo -e "${GREEN}All tests passed!${NC}"
        return 0
    else
        echo -e "${RED}Some tests failed${NC}"
        return 1
    fi
}

main() {
    echo "========================================"
    echo "NATS PubSub App - Integration Tests"
    echo "========================================"
    echo ""

    cd "$PROJECT_ROOT"

    # Run all tests
    test_containers_running || true
    test_nats_health || true
    test_nats_monitoring || true
    test_client_connections || true
    test_subscriptions || true
    test_publisher_publishing || true
    test_subscriber_receiving || true
    test_message_flow_rate || true
    test_json_log_format || true
    test_log_fields || true
    test_metrics_logging || true
    test_latency_tracking || true
    test_docker_labels || true

    print_summary
}

main "$@"
