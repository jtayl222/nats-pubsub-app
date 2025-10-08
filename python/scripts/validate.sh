#!/bin/bash
set -euo pipefail

# validate.sh - Validate NATS pubsub application health
# Usage: ./validate.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

TESTS_PASSED=0
TESTS_FAILED=0

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_pass() {
    echo -e "${GREEN}[PASS]${NC} $1"
    ((TESTS_PASSED++))
}

log_fail() {
    echo -e "${RED}[FAIL]${NC} $1"
    ((TESTS_FAILED++))
}

check_container() {
    local container_name=$1
    log_info "Checking container: $container_name"

    if ! docker ps --filter "name=$container_name" --filter "status=running" --format '{{.Names}}' | grep -q "^${container_name}$"; then
        log_fail "Container $container_name is not running"
        return 1
    fi

    local health=$(docker inspect --format='{{.State.Health.Status}}' "$container_name" 2>/dev/null || echo "no-healthcheck")

    case "$health" in
        healthy)
            log_pass "Container $container_name is healthy"
            return 0
            ;;
        unhealthy)
            log_fail "Container $container_name is unhealthy"
            return 1
            ;;
        no-healthcheck)
            log_pass "Container $container_name is running (no healthcheck)"
            return 0
            ;;
        *)
            log_fail "Container $container_name status: $health"
            return 1
            ;;
    esac
}

check_nats_api() {
    log_info "Checking NATS monitoring API..."

    local response=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8222/varz" || echo "000")

    if [ "$response" = "200" ]; then
        log_pass "NATS API is accessible"
        return 0
    else
        log_fail "NATS API returned HTTP $response"
        return 1
    fi
}

check_nats_connections() {
    log_info "Checking NATS client connections..."

    local conn_data=$(curl -s "http://localhost:8222/connz" || echo "{}")
    local num_connections=$(echo "$conn_data" | grep -o '"num_connections":[0-9]*' | cut -d':' -f2 || echo "0")

    if [ "$num_connections" -ge 2 ]; then
        log_pass "NATS has $num_connections client connections (publisher + subscriber)"
        return 0
    else
        log_fail "NATS has only $num_connections connections (expected >= 2)"
        return 1
    fi
}

check_nats_subscriptions() {
    log_info "Checking NATS subscriptions..."

    local subs_data=$(curl -s "http://localhost:8222/subsz" || echo "{}")
    local num_subs=$(echo "$subs_data" | grep -o '"num_subscriptions":[0-9]*' | cut -d':' -f2 || echo "0")

    if [ "$num_subs" -ge 1 ]; then
        log_pass "NATS has $num_subs active subscription(s)"
        return 0
    else
        log_fail "NATS has no active subscriptions"
        return 1
    fi
}

check_publisher_logs() {
    log_info "Checking publisher is sending messages..."

    local recent_logs=$(docker logs --tail 20 nats-publisher 2>&1 || echo "")

    if echo "$recent_logs" | grep -q "Message published"; then
        log_pass "Publisher is actively publishing messages"
        return 0
    else
        log_fail "Publisher logs show no recent published messages"
        return 1
    fi
}

check_subscriber_logs() {
    log_info "Checking subscriber is receiving messages..."

    local recent_logs=$(docker logs --tail 20 nats-subscriber 2>&1 || echo "")

    if echo "$recent_logs" | grep -q "Message received"; then
        log_pass "Subscriber is actively receiving messages"
        return 0
    else
        log_fail "Subscriber logs show no recent received messages"
        return 1
    fi
}

check_json_logging() {
    log_info "Checking JSON log format..."

    local publisher_log=$(docker logs --tail 5 nats-publisher 2>&1 | head -1)

    if echo "$publisher_log" | jq . > /dev/null 2>&1; then
        log_pass "Logs are in valid JSON format"
        return 0
    else
        log_fail "Logs are not in valid JSON format"
        return 1
    fi
}

check_docker_volumes() {
    log_info "Checking Docker volumes..."

    if docker volume inspect nats-pubsub-app_nats-data &>/dev/null; then
        log_pass "NATS data volume exists"
        return 0
    else
        log_fail "NATS data volume not found"
        return 1
    fi
}

test_message_flow() {
    log_info "Testing end-to-end message flow..."

    # Get initial message counts
    local pub_before=$(docker logs nats-publisher 2>&1 | grep -c "Message published" || echo "0")
    local sub_before=$(docker logs nats-subscriber 2>&1 | grep -c "Message received" || echo "0")

    # Wait for some messages
    sleep 10

    # Get new message counts
    local pub_after=$(docker logs nats-publisher 2>&1 | grep -c "Message published" || echo "0")
    local sub_after=$(docker logs nats-subscriber 2>&1 | grep -c "Message received" || echo "0")

    local pub_new=$((pub_after - pub_before))
    local sub_new=$((sub_after - sub_before))

    if [ "$pub_new" -gt 0 ] && [ "$sub_new" -gt 0 ]; then
        log_pass "Message flow verified: ${pub_new} published, ${sub_new} received"
        return 0
    else
        log_fail "Message flow issue: ${pub_new} published, ${sub_new} received"
        return 1
    fi
}

print_summary() {
    local total=$((TESTS_PASSED + TESTS_FAILED))

    echo ""
    echo "================================"
    echo "Validation Summary"
    echo "================================"
    echo "Total checks: $total"
    echo -e "Passed: ${GREEN}$TESTS_PASSED${NC}"
    echo -e "Failed: ${RED}$TESTS_FAILED${NC}"
    echo "================================"

    if [ $TESTS_FAILED -eq 0 ]; then
        log_pass "All validation checks passed"
        return 0
    else
        log_fail "Some validation checks failed"
        return 1
    fi
}

main() {
    log_info "Starting validation of NATS pubsub application"
    echo ""

    cd "$PROJECT_ROOT"

    # Run all checks
    check_container "nats-server" || true
    check_container "nats-publisher" || true
    check_container "nats-subscriber" || true
    check_nats_api || true
    check_nats_connections || true
    check_nats_subscriptions || true
    check_publisher_logs || true
    check_subscriber_logs || true
    check_json_logging || true
    check_docker_volumes || true
    test_message_flow || true

    print_summary
}

main "$@"
