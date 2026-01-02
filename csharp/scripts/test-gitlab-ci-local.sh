#!/bin/bash
# Local GitLab CI/CD Pipeline Testing Script
# This script simulates the GitLab CI pipeline locally on macOS/Linux
#
# Usage:
#   ./scripts/test-gitlab-ci-local.sh [stage]
#
# Stages:
#   all          - Run all stages (default)
#   build        - Build only
#   unit-test    - Unit tests only
#   component-test - Component tests (starts NATS container)
#   validate     - Validate .gitlab-ci.yml syntax only

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
NATS_CONTAINER_NAME="gitlab-ci-nats-test"
RESULTS_DIR="$PROJECT_ROOT/results"
STARTED_NATS=false

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

cleanup() {
    log_info "Cleaning up..."
    if [ "$STARTED_NATS" = true ]; then
        docker rm -f "$NATS_CONTAINER_NAME" 2>/dev/null || true
    fi
}

validate_gitlab_ci() {
    log_info "Validating .gitlab-ci.yml syntax..."

    if command -v gitlab-runner &> /dev/null; then
        # Use gitlab-runner to validate
        cd "$PROJECT_ROOT"
        if gitlab-runner verify 2>/dev/null; then
            log_success "gitlab-runner is registered and ready"
        else
            log_warn "gitlab-runner not registered (OK for local testing)"
        fi
    fi

    # Basic YAML syntax check using ruby (comes with macOS) or python
    if command -v ruby &> /dev/null; then
        ruby -ryaml -e "YAML.load_file('$PROJECT_ROOT/.gitlab-ci.yml')" 2>/dev/null && \
            log_success ".gitlab-ci.yml is valid YAML" || \
            { log_error "Invalid YAML syntax"; exit 1; }
    elif command -v python3 &> /dev/null; then
        python3 -c "import yaml; yaml.safe_load(open('$PROJECT_ROOT/.gitlab-ci.yml'))" 2>/dev/null && \
            log_success ".gitlab-ci.yml is valid YAML" || \
            log_warn "Could not validate YAML (pyyaml not installed)"
    else
        log_warn "No YAML validator available (ruby or python with pyyaml)"
    fi
}

stage_build() {
    log_info "=== STAGE: build ==="
    cd "$PROJECT_ROOT"

    log_info "Restoring packages..."
    dotnet restore NatsHttpGateway/NatsHttpGateway.sln

    log_info "Building solution..."
    dotnet build NatsHttpGateway/NatsHttpGateway.sln --configuration Release --no-restore

    log_success "Build completed successfully"
}

stage_unit_test() {
    log_info "=== STAGE: unit-test ==="
    cd "$PROJECT_ROOT"

    mkdir -p "$RESULTS_DIR"

    log_info "Running unit tests..."
    dotnet test NatsHttpGateway.Tests/NatsHttpGateway.Tests.csproj \
        --configuration Release \
        --logger "trx;LogFileName=unit-test-results.trx" \
        --logger "console;verbosity=normal" \
        --collect:"XPlat Code Coverage" \
        --results-directory "$RESULTS_DIR" \
        --filter "Category!=Component" \
        || { log_error "Unit tests failed"; exit 1; }

    log_success "Unit tests passed"
    log_info "Results saved to: $RESULTS_DIR/"
}

start_nats() {
    log_info "Starting NATS JetStream container..."

    # Check if NATS is already running on the expected ports
    if curl -s http://localhost:8222/healthz > /dev/null 2>&1; then
        log_success "NATS is already running on localhost:4222"
        return 0
    fi

    # Stop existing test container if it exists but isn't responding
    docker rm -f "$NATS_CONTAINER_NAME" 2>/dev/null || true

    # Check if ports are in use by another container
    if docker ps --format '{{.Ports}}' | grep -q "4222\|8222"; then
        log_warn "Ports 4222/8222 in use by another container. Attempting to use existing NATS..."
        for i in $(seq 1 10); do
            if curl -s http://localhost:8222/healthz > /dev/null 2>&1; then
                log_success "Existing NATS JetStream is ready!"
                return 0
            fi
            sleep 1
        done
        log_error "Could not connect to existing NATS on ports 4222/8222"
        log_info "Stop other NATS containers with: docker ps | grep nats"
        exit 1
    fi

    # Start NATS with JetStream
    docker run -d \
        --name "$NATS_CONTAINER_NAME" \
        -p 4222:4222 \
        -p 8222:8222 \
        nats:latest \
        --jetstream -m 8222

    STARTED_NATS=true

    # Wait for NATS to be ready
    log_info "Waiting for NATS to be ready..."
    for i in $(seq 1 30); do
        if curl -s http://localhost:8222/healthz > /dev/null 2>&1; then
            log_success "NATS JetStream is ready!"
            return 0
        fi
        echo -n "."
        sleep 1
    done

    log_error "NATS failed to start within 30 seconds"
    exit 1
}

stage_component_test() {
    log_info "=== STAGE: component-test ==="
    cd "$PROJECT_ROOT"

    mkdir -p "$RESULTS_DIR"

    # Start NATS
    start_nats

    # Set environment variable for tests
    export NATS_URL="nats://localhost:4222"

    log_info "Running component tests..."
    dotnet test NatsHttpGateway.Tests/NatsHttpGateway.Tests.csproj \
        --configuration Release \
        --logger "trx;LogFileName=component-test-results.trx" \
        --logger "console;verbosity=normal" \
        --results-directory "$RESULTS_DIR" \
        --filter "Category=Component" \
        || {
            log_error "Component tests failed"
            cleanup
            exit 1
        }

    log_success "Component tests passed"
    log_info "Results saved to: $RESULTS_DIR/"

    cleanup
}

run_all() {
    log_info "Running full pipeline simulation..."
    echo ""

    validate_gitlab_ci
    echo ""

    stage_build
    echo ""

    stage_unit_test
    echo ""

    stage_component_test
    echo ""

    log_success "=== ALL STAGES COMPLETED SUCCESSFULLY ==="
}

show_help() {
    echo "GitLab CI Local Testing Script"
    echo ""
    echo "Usage: $0 [stage]"
    echo ""
    echo "Stages:"
    echo "  all             Run all stages (default)"
    echo "  build           Build the solution"
    echo "  unit-test       Run unit tests"
    echo "  component-test  Run component tests (starts NATS container)"
    echo "  validate        Validate .gitlab-ci.yml syntax"
    echo "  help            Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0              # Run all stages"
    echo "  $0 build        # Build only"
    echo "  $0 component-test  # Run component tests"
}

# Trap to ensure cleanup on exit
trap cleanup EXIT

# Main
case "${1:-all}" in
    all)
        run_all
        ;;
    build)
        stage_build
        ;;
    unit-test)
        stage_unit_test
        ;;
    component-test)
        stage_component_test
        ;;
    validate)
        validate_gitlab_ci
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        log_error "Unknown stage: $1"
        show_help
        exit 1
        ;;
esac
