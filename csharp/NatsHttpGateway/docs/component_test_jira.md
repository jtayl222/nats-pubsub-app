# Jira Ticket: Add Component Testing to NatsHttpGateway CI/CD Pipeline

## Summary

**Title:** Add Component Testing Stage to NatsHttpGateway GitLab CI/CD Pipeline

**Type:** Story

**Priority:** Medium

**Labels:** `ci-cd`, `testing`, `component-tests`, `quality`, `nats-http-gateway`

---

## Description

Add a component testing stage to the existing GitLab CI/CD pipeline for the NatsHttpGateway application. Component tests will validate the integration between the NatsHttpGateway REST/WebSocket API and a real NATS JetStream server, ensuring pub/sub operations, consumer management, stream operations, and health endpoints function correctly in a near-production environment.

---

## Background

The current CI/CD pipeline includes:
- Build stage
- SonarQube analysis
- Fortify security scanning
- Unit tests (NUnit with Moq)
- Docker image publish to Nexus

The existing unit tests in `NatsHttpGateway.Tests` mock the `INatsService` interface to test controller logic in isolation. However, we lack validation against a real NATS JetStream server. Component testing will catch integration issues before deployment, such as:
- Serialization mismatches between the API and NATS
- Incorrect JetStream configuration assumptions
- WebSocket protocol handling issues
- Consumer acknowledgment behavior differences

---

## Acceptance Criteria

- [ ] New `component-test` stage added to `.gitlab-ci.yml` after unit tests and before image publish
- [ ] NATS JetStream server runs as a GitLab service container during component test execution
- [ ] Component tests execute against the live NATS service using `WebApplicationFactory<Program>`
- [ ] Test results published as GitLab artifacts (JUnit XML format)
- [ ] Pipeline fails if any component test fails
- [ ] Component test stage runs after SonarQube/Fortify (can run in parallel where possible)
- [ ] Documentation updated with instructions for running component tests locally

---

## Technical Implementation Notes

### 1. GitLab Service Container

```yaml
component-test:
  stage: component-test
  services:
    - name: nats:latest
      alias: nats
      command: ["--jetstream", "-m", "8222"]
  variables:
    NATS_URL: "nats://nats:4222"
```

### 2. Test Project Structure

Component tests are in a **separate project**: `NatsHttpGateway.ComponentTests`

This approach:
- Allows developers without Docker to skip component tests entirely
- Clear separation of concerns between unit and integration testing
- Supports different test runners and CI/CD configurations
- Enables conditional execution based on environment capabilities

### 3. Test Execution

```yaml
script:
  - dotnet restore NatsHttpGateway.ComponentTests/NatsHttpGateway.ComponentTests.csproj
  - dotnet build NatsHttpGateway.ComponentTests/NatsHttpGateway.ComponentTests.csproj --configuration Release
  - dotnet test NatsHttpGateway.ComponentTests/NatsHttpGateway.ComponentTests.csproj
      --configuration Release
      --no-build
      --logger "trx;LogFileName=component-test-results.trx"
      --logger "console;verbosity=detailed"
      --results-directory ./results
artifacts:
  when: always
  paths:
    - results/
```

### 3.1 JWT Authentication Support

Component tests support JWT authentication via environment variable:

```yaml
variables:
  NATS_URL: "nats://nats:4222"
  JWT_TOKEN: "${NATS_JWT_TOKEN}"  # Set at project/group level
```

### 4. Required Component Test Coverage

| Test Area | Endpoints | Priority |
|-----------|-----------|----------|
| Message publish/consume round-trip | `POST /messages/{subject}`, `GET /messages/{subject}` | Critical |
| Consumer acknowledgment and replay | `POST /streams/{stream}/consumers`, fetch with ack | Critical |
| Consumer reset operations | `POST /streams/{stream}/consumers/{name}/reset` | Critical |
| Stream creation/deletion | `GET/POST /streams`, `DELETE /streams/{name}` | High |
| Protobuf message handling | `POST/GET /messages/proto/{subject}` | High |
| Health endpoint with live NATS | `GET /health` | Medium |
| WebSocket subscription | `WS /ws/messages/{subject}` | Medium |

### 5. Test Infrastructure

Create base test fixture for NATS connectivity:

```csharp
[TestFixture]
[Category("Component")]
public abstract class NatsComponentTestBase
{
    protected WebApplicationFactory<Program> Factory = null!;
    protected HttpClient Client = null!;
    protected NatsConnection NatsConnection = null!;
    protected INatsJSContext JetStream = null!;

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        var natsUrl = Environment.GetEnvironmentVariable("NATS_URL")
            ?? "nats://localhost:4222";

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Nats:Url", natsUrl);
            });

        Client = Factory.CreateClient();
        NatsConnection = new NatsConnection(new NatsOpts { Url = natsUrl });
        await NatsConnection.ConnectAsync();
        JetStream = new NatsJSContext(NatsConnection);
    }

    /// <summary>
    /// Helper for eventual consistency - retries until condition passes or times out.
    /// </summary>
    protected async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition not met within timeout period");
    }
}
```

---

## Dependencies

- Existing test infrastructure: NUnit 4.0, Microsoft.AspNetCore.Mvc.Testing 8.0
- NATS.Net 2.4.0 (already in use)
- GitLab Runner with Docker executor
- NATS official Docker image (`nats:latest`)

---

## Out of Scope

- End-to-end UI testing
- Performance/load testing
- Changes to existing unit tests
- Testing of other projects (Publisher, Subscriber, MessageLogger, etc.)

---

## Definition of Done

- [ ] Component test base fixture created with NATS connectivity (including `WaitForAsync` helper)
- [ ] Component tests covering all critical and high-priority areas:
  - Message publish and fetch round-trip
  - Consumer creation and message acknowledgment
  - Consumer reset operations
  - Stream CRUD operations against real NATS
  - Protobuf message handling
  - Health endpoint with live connection verification
  - WebSocket message streaming (including error handling)
- [ ] Component test stage added to `.gitlab-ci.yml`
- [ ] All component tests pass in CI/CD pipeline
- [ ] Pipeline blocks merge on component test failure
- [ ] Test results visible in GitLab merge request UI
- [ ] README updated with local component test instructions

---

## Local Development Instructions

### Using the Test Script (Recommended)

```bash
# Run component tests (handles Docker and NATS automatically)
./scripts/test-gitlab-ci-local.sh component-test

# Run all stages
./scripts/test-gitlab-ci-local.sh all

# Run only unit tests (no Docker required)
./scripts/test-gitlab-ci-local.sh unit-test
```

### Manual Setup

```bash
# Start NATS with JetStream
docker run -d --name nats-component-test -p 4222:4222 -p 8222:8222 nats:latest --jetstream -m 8222

# Run component tests
cd NatsHttpGateway.ComponentTests
export NATS_URL="nats://localhost:4222"
dotnet test

# Cleanup
docker rm -f nats-component-test
```

### JWT Authentication

```bash
# With JWT token
export JWT_TOKEN=$(cat ~/.nats/my.jwt)
./scripts/test-gitlab-ci-local.sh component-test
```

### Windows 11 Support

For developers without Docker:
1. Run unit tests only: `./scripts/test-gitlab-ci-local.sh unit-test`
2. Or use an external NATS server: `NATS_URL=nats://server:4222 SKIP_DOCKER_CHECK=true ./scripts/test-gitlab-ci-local.sh component-test`
