# GitLab Runner Setup for Local Development

This guide explains how to run CI/CD jobs locally using GitLab Runner, ensuring `.gitlab-ci.yml` remains the single source of truth.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Development Setup                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────────┐      ┌──────────────────────────────┐ │
│  │   Windows 11 Host    │      │      Linux VM (VirtualBox)   │ │
│  │                      │      │                              │ │
│  │  - .NET 8.0 SDK      │ SSH  │  - Docker                    │ │
│  │  - Git               │─────▶│  - GitLab Runner             │ │
│  │  - VS Code / Rider   │      │  - NATS JetStream            │ │
│  │                      │      │                              │ │
│  └──────────────────────┘      └──────────────────────────────┘ │
│           │                              │                       │
│           │         NATS Protocol        │                       │
│           └──────────────────────────────┘                       │
│                    192.168.56.x                                  │
│                  (Host-Only Network)                             │
└─────────────────────────────────────────────────────────────────┘
```

## Linux VM Setup

### 1. Install Docker

```bash
# Ubuntu/Debian
sudo apt-get update
sudo apt-get install -y docker.io
sudo systemctl enable docker
sudo systemctl start docker
sudo usermod -aG docker $USER

# RHEL/Rocky/Fedora
sudo dnf install -y docker
sudo systemctl enable docker
sudo systemctl start docker
sudo usermod -aG docker $USER
```

> **Note:** Log out and back in for the group change to take effect.

### 2. Install GitLab Runner

```bash
# Ubuntu/Debian
curl -L "https://packages.gitlab.com/install/repositories/runner/gitlab-runner/script.deb.sh" | sudo bash
sudo apt-get install -y gitlab-runner

# RHEL/Rocky/Fedora
curl -L "https://packages.gitlab.com/install/repositories/runner/gitlab-runner/script.rpm.sh" | sudo bash
sudo dnf install -y gitlab-runner

# Verify installation
gitlab-runner --version
```

### 3. Start NATS JetStream

```bash
docker run -d \
  --name nats \
  --restart unless-stopped \
  -p 4222:4222 \
  -p 8222:8222 \
  nats:latest \
  --jetstream -m 8222

# Verify NATS is running
curl http://localhost:8222/healthz
```

### 4. Configure VirtualBox Networking

For Windows host to reach the Linux VM:

1. In VirtualBox: **File > Host Network Manager**
2. Create a host-only network (e.g., `vboxnet0` with `192.168.56.1/24`)
3. In VM settings: **Network > Adapter 2 > Host-only Adapter**
4. Inside VM, verify IP: `ip addr show` (should show `192.168.56.x`)

## Running CI Jobs Locally

### Option 1: gitlab-runner exec (Recommended)

Uses `.gitlab-ci.yml` directly - single source of truth:

```bash
# Navigate to repo root
cd /path/to/nats-pubsub-app/csharp

# Run specific jobs
gitlab-runner exec docker build
gitlab-runner exec docker unit-test
gitlab-runner exec docker security-test
gitlab-runner exec docker component-test
```

**Note:** `gitlab-runner exec` runs jobs in isolated Docker containers, matching CI behavior exactly.

### Option 2: From Windows via SSH

```powershell
# Run a specific job
ssh user@192.168.56.101 "cd /path/to/repo && gitlab-runner exec docker unit-test"

# Run component tests
ssh user@192.168.56.101 "cd /path/to/repo && gitlab-runner exec docker component-test"
```

### Option 3: Direct dotnet test (Windows)

For quick iteration without full CI simulation:

```powershell
# Set NATS URL to point to Linux VM
$env:NATS_URL = "nats://192.168.56.101:4222"

# Unit tests (no NATS required)
dotnet test NatsHttpGateway.Tests/NatsHttpGateway.Tests.csproj `
  --filter "Category!=Component&Category!=Security"

# Security tests (no NATS required)
dotnet test NatsHttpGateway.Tests/NatsHttpGateway.Tests.csproj `
  --filter "Category=Security"
dotnet test NatsHttpGateway.ComponentTests/NatsHttpGateway.ComponentTests.csproj `
  --filter "Category=Security"

# Component tests (requires NATS)
dotnet test NatsHttpGateway.ComponentTests/NatsHttpGateway.ComponentTests.csproj `
  --filter "Category=Component"
```

## Registering Runner with GitLab Server (Optional)

To run jobs triggered by GitLab CI/CD:

```bash
sudo gitlab-runner register \
  --url "https://gitlab.com/" \
  --registration-token "YOUR_PROJECT_TOKEN" \
  --executor "docker" \
  --docker-image "mcr.microsoft.com/dotnet/sdk:8.0" \
  --description "nats-dev-runner" \
  --tag-list "docker,dotnet" \
  --docker-privileged
```

Get the registration token from: **GitLab Project > Settings > CI/CD > Runners**

## CI Job Reference

| Job | Stage | NATS Required | Description |
|-----|-------|:-------------:|-------------|
| `build` | build | No | Restore and build solution |
| `unit-test` | test | No | Unit tests (excludes Security & Component) |
| `security-test` | security-test | No | Security tests (JWT, auth attributes) |
| `component-test` | component-test | Yes | Integration tests with real NATS |
| `publish-image` | publish | No | Build and push Docker image |

## Troubleshooting

### NATS Connection Refused

```bash
# Check NATS is running
docker ps | grep nats
curl http://192.168.56.101:8222/healthz

# Check firewall on Linux VM (Ubuntu/Debian)
sudo ufw status
sudo ufw allow 4222/tcp
sudo ufw allow 8222/tcp

# Check firewall on Linux VM (RHEL/Rocky/Fedora)
sudo firewall-cmd --list-ports
sudo firewall-cmd --permanent --add-port=4222/tcp
sudo firewall-cmd --permanent --add-port=8222/tcp
sudo firewall-cmd --reload
```

### gitlab-runner exec Fails

```bash
# Ensure Docker is accessible
docker ps

# Check runner can use Docker
gitlab-runner exec docker --help

# Run with debug output
gitlab-runner --debug exec docker build
```

### Windows Cannot Reach VM

```powershell
# Test connectivity
ping 192.168.56.101
Test-NetConnection -ComputerName 192.168.56.101 -Port 4222

# Check VirtualBox host-only adapter is enabled
ipconfig | Select-String "192.168.56"
```

## Why This Approach?

**Single Source of Truth:** `.gitlab-ci.yml` defines all build/test logic once. No parallel scripts to maintain.

**Reproducibility:** `gitlab-runner exec` runs jobs in the same Docker environment as CI/CD, eliminating "works on my machine" issues.

**Flexibility:** Developers can run full CI simulation or quick `dotnet test` commands depending on their needs.
