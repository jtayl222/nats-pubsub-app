#!/bin/bash
set -euo pipefail

# deploy.sh - Deploy NATS pubsub application
# Usage: ./deploy.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

check_dependencies() {
    log_info "Checking dependencies..."

    local missing_deps=()

    if ! command -v docker &> /dev/null; then
        missing_deps+=("docker")
    fi

    if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
        missing_deps+=("docker-compose")
    fi

    if [ ${#missing_deps[@]} -ne 0 ]; then
        log_error "Missing dependencies: ${missing_deps[*]}"
        exit 1
    fi

    log_info "All dependencies satisfied"
}

create_env_file() {
    log_info "Setting up environment configuration..."

    if [ ! -f "$PROJECT_ROOT/.env" ]; then
        log_warn ".env file not found, creating from .env.example"
        cp "$PROJECT_ROOT/.env.example" "$PROJECT_ROOT/.env"

        # Set hostname
        local hostname=$(hostname)
        sed -i.bak "s/HOSTNAME=.*/HOSTNAME=${hostname}/" "$PROJECT_ROOT/.env"
        rm -f "$PROJECT_ROOT/.env.bak"

        log_info "Created .env with HOSTNAME=${hostname}"
    else
        log_info ".env file already exists"
    fi
}

build_images() {
    log_info "Building Docker images..."
    cd "$PROJECT_ROOT"

    docker-compose build --no-cache

    if [ $? -eq 0 ]; then
        log_info "Images built successfully"
    else
        log_error "Failed to build images"
        exit 1
    fi
}

stop_existing() {
    log_info "Stopping existing containers..."
    cd "$PROJECT_ROOT"

    if docker-compose ps -q 2>/dev/null | grep -q .; then
        docker-compose down
        log_info "Stopped existing containers"
    else
        log_info "No existing containers found"
    fi
}

start_services() {
    log_info "Starting NATS pubsub services..."
    cd "$PROJECT_ROOT"

    docker-compose up -d

    if [ $? -eq 0 ]; then
        log_info "Services started successfully"
    else
        log_error "Failed to start services"
        exit 1
    fi
}

wait_for_health() {
    log_info "Waiting for services to become healthy..."

    local max_wait=60
    local elapsed=0
    local interval=5

    while [ $elapsed -lt $max_wait ]; do
        local nats_healthy=$(docker inspect --format='{{.State.Health.Status}}' nats-server 2>/dev/null || echo "starting")

        if [ "$nats_healthy" = "healthy" ]; then
            log_info "NATS server is healthy"
            return 0
        fi

        log_info "NATS: $nats_healthy (${elapsed}s/${max_wait}s)"
        sleep $interval
        elapsed=$((elapsed + interval))
    done

    log_error "Services did not become healthy within ${max_wait}s"
    return 1
}

verify_deployment() {
    log_info "Verifying deployment..."

    # Check containers are running
    local running_containers=$(docker-compose ps --filter "status=running" -q | wc -l | tr -d ' ')

    if [ "$running_containers" -eq 3 ]; then
        log_info "All 3 containers running (nats, publisher, subscriber)"
    else
        log_warn "Only ${running_containers}/3 containers running"
    fi

    # Check NATS monitoring endpoint
    if curl -s http://localhost:8222/varz > /dev/null 2>&1; then
        log_info "NATS monitoring endpoint accessible"
    else
        log_warn "NATS monitoring endpoint not accessible"
    fi
}

show_info() {
    log_info "Deployment complete!"
    echo ""
    echo "Services:"
    echo "  NATS Server:    nats://localhost:4222"
    echo "  NATS Monitoring: http://localhost:8222"
    echo ""
    echo "Useful commands:"
    echo "  View logs:          docker-compose logs -f"
    echo "  View publisher:     docker-compose logs -f publisher"
    echo "  View subscriber:    docker-compose logs -f subscriber"
    echo "  Stop:               docker-compose down"
    echo "  Restart:            docker-compose restart"
    echo "  Status:             docker-compose ps"
    echo ""
    echo "NATS Monitoring:"
    echo "  Server info:        curl http://localhost:8222/varz"
    echo "  Connections:        curl http://localhost:8222/connz"
    echo "  Subscriptions:      curl http://localhost:8222/subsz"
    echo ""
}

main() {
    log_info "Starting deployment of NATS pubsub application"

    check_dependencies
    create_env_file
    build_images
    stop_existing
    start_services
    wait_for_health
    verify_deployment
    show_info

    log_info "Deployment completed successfully"
}

main "$@"
