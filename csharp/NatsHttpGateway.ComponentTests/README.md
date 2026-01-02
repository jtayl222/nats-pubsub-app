# NatsHttpGateway Component Tests

## Table of Contents

- [Why Component Tests?](#why-component-tests)
- [Testing Levels Explained](#testing-levels-explained)
- [Execution Flow Comparison](#execution-flow-comparison)
- [How Component Tests Work](#how-component-tests-work)
- [Running Component Tests](#running-component-tests)
- [Adding New Component Tests](#adding-new-component-tests)
- [Code Coverage Strategy](#code-coverage-strategy)

---

## Why Component Tests?

Component tests fill a critical gap between unit tests and UAT that neither can address effectively.

### The Testing Gap Problem

A unit test for `MessagesController` mocks `INatsService` and verifies that when `PublishAsync` returns success, the controller returns HTTP 200. The test passes. But in production, the application fails because:

- The JSON serialization format doesn't match what NATS expects
- The JetStream stream configuration assumptions were wrong
- The consumer acknowledgment behavior differs from the mock

**Unit tests verify our code works *if external dependencies behave as we assume*. Component tests verify those assumptions are correct.**

### What Each Test Level Catches

| Issue Type | Unit Tests | Component Tests | UAT |
|------------|:----------:|:---------------:|:---:|
| Logic errors in controller code | ✅ | ✅ | ✅ |
| Incorrect mock assumptions | ❌ | ✅ | ✅ |
| NATS protocol/serialization issues | ❌ | ✅ | ✅ |
| JetStream configuration problems | ❌ | ✅ | ✅ |
| Infrastructure/network issues | ❌ | ❌ | ✅ |
| Cross-service integration | ❌ | ❌ | ✅ |
| Business workflow validation | ❌ | ❌ | ✅ |

### Why Not Just Use UAT?

UAT tests are slow (minutes), expensive (full environment), and flaky (network issues). Component tests provide **fast, reliable feedback** (seconds) while catching the same integration issues.

---

## Testing Levels Explained

### Unit Tests (`NatsHttpGateway.Tests`)

- Mock all external dependencies
- Run in milliseconds
- Test edge cases and error handling extensively
- **Example**: Verify `MessagesController.Publish()` returns BadRequest when message_id is empty

### Component Tests (`NatsHttpGateway.ComponentTests`)

- Use real NATS JetStream server
- Test the full request/response cycle
- Verify serialization, protocol handling, and configuration
- Run in seconds
- **Example**: Publish a message via the API, verify it exists in NATS

### UAT (User Acceptance Testing)

- Full deployment with all services
- Tests business workflows end-to-end
- Validates non-functional requirements
- **Example**: Verify payment message flows through gateway to payment service

---

## Execution Flow Comparison

```mermaid
flowchart TB
    subgraph "Test Request Origin"
        UT[Unit Test]
        CT[Component Test]
        UAT[UAT / Production]
    end

    subgraph "Application Layer"
        Controller[Controller]
        Service[NatsService]
        Serializer[JSON Serializer]
    end

    subgraph "External Dependencies"
        MockNats[Mock INatsService]
        RealNats[Real NATS JetStream]
        ProdNats[Production NATS Cluster]
        OtherServices[Other Microservices]
    end

    %% Unit Test Path
    UT -->|"Direct call"| Controller
    Controller -->|"Injected mock"| MockNats
    MockNats -.->|"Returns canned response"| Controller

    %% Component Test Path
    CT -->|"HTTP Request"| Controller
    Controller --> Service
    Service --> Serializer
    Serializer -->|"Real protocol"| RealNats
    RealNats -->|"Real response"| Serializer

    %% UAT Path
    UAT -->|"HTTP via Load Balancer"| Controller
    Controller --> Service
    Service --> Serializer
    Serializer -->|"Real protocol"| ProdNats
    ProdNats <-->|"Message flow"| OtherServices

    style MockNats fill:#ffcccc,stroke:#cc0000
    style RealNats fill:#ccffcc,stroke:#00cc00
    style ProdNats fill:#ccccff,stroke:#0000cc
```

### What Gets Tested at Each Level

```mermaid
flowchart LR
    subgraph Unit["Unit Tests"]
        direction TB
        U1[Controller Logic]
        U2[Validation Rules]
        U3[Error Handling]
    end

    subgraph Component["Component Tests"]
        direction TB
        C1[Full HTTP Pipeline]
        C2[JSON Serialization]
        C3[NATS Protocol]
        C4[JetStream Config]
    end

    subgraph UAT["UAT Tests"]
        direction TB
        A1[Multi-Service Flows]
        A2[Business Workflows]
        A3[Performance/Scale]
    end

    Unit -->|"Assumptions validated by"| Component
    Component -->|"Integration validated by"| UAT

    style Unit fill:#ffe6cc
    style Component fill:#e6ffcc
    style UAT fill:#cce6ff
```

---

## How Component Tests Work

### Verification Strategy: Triangulation with nats-box

Component tests use **two independent verification paths** to catch deserialization inconsistencies:

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Component Test Verification                      │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌──────────────┐        HTTP POST        ┌──────────────────┐     │
│  │              │ ──────────────────────▶ │                  │     │
│  │  Test Class  │                         │ NatsHttpGateway  │     │
│  │              │ ◀────────────────────── │ (In-Memory Host) │     │
│  └──────────────┘        HTTP 200         └──────────────────┘     │
│         │                                          │                │
│         │                                          │                │
│    ┌────┴────┐                                     │                │
│    │         │                                     │                │
│    ▼         ▼                                     ▼                │
│ ┌──────┐  ┌─────────┐                    ┌─────────────────┐       │
│ │Direct│  │nats-box │                    │   NatsService   │       │
│ │ NATS │  │ (Docker)│                    │                 │       │
│ │Client│  │         │                    └─────────────────┘       │
│ └──────┘  └─────────┘                             │                │
│    │           │                                  │                │
│    │           │                                  │                │
│    ▼           ▼                                  ▼                │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                      NATS JetStream                          │   │
│  │              (Local Docker or Linux VM)                      │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                      │
│  Verification 1: Direct NATS client reads message                   │
│  Verification 2: nats-box CLI reads message (independent parser)    │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Why Two Verification Paths?

We have observed cases where our NATS client deserialization and nats-box deserialization produce different results. By verifying with **both**:

1. **Direct NATS connection** (C# NATS.Client library)
2. **nats-box CLI** (Go-based official NATS tooling)

...we catch serialization format issues that a single verification path would miss.

### Key Components

#### `NatsComponentTestBase`

Provides:
- **WebApplicationFactory**: In-memory test server hosting the full application
- **HttpClient**: For making API requests
- **NatsConnection + JetStream**: Direct NATS connection for verification
- **Test Isolation**: Unique stream names per test

#### Test Pattern Example

```csharp
[Test]
public async Task PublishMessage_VerifyWithDirectNatsRead()
{
    // 1. Setup: Create stream directly in NATS
    await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, ...));

    // 2. Act: Publish via the HTTP API
    var response = await Client.PostAsJsonAsync($"/api/messages/{subject}", request);

    // 3. Verify Path 1: Read directly from NATS
    var consumer = await JetStream.CreateOrUpdateConsumerAsync(...);
    var msg = await consumer.NextAsync<byte[]>();
    Assert.That(msg.HasValue, Is.True);

    // 4. Verify Path 2: Read via nats-box (in separate test)
    var natsBoxOutput = await RunNatsBoxCommandAsync($"stream get {TestStreamName}");
    Assert.That(natsBoxOutput, Does.Contain(expectedValue));
}
```

---

## Running Component Tests

### Prerequisites

- .NET 8.0 SDK
- Access to NATS JetStream server (local Docker or Linux VM)

### Using the Test Script

```bash
# Run component tests (uses localhost:4222 by default)
./scripts/test-gitlab-ci-local.sh component-test

# Run against VM
NATS_URL=nats://192.168.1.100:4222 ./scripts/test-gitlab-ci-local.sh component-test

# Run all stages
./scripts/test-gitlab-ci-local.sh all

# Run unit tests only
./scripts/test-gitlab-ci-local.sh unit-test
```

### Manual Execution

```bash
cd NatsHttpGateway.ComponentTests
export NATS_URL="nats://localhost:4222"  # or nats://<VM-IP>:4222
dotnet test
```

### With JWT Authentication

```bash
export JWT_TOKEN=$(cat ~/.nats/credentials.jwt)
./scripts/test-gitlab-ci-local.sh component-test
```

### nats-box Verification Tests

The nats-box tests require Docker to be available (either locally or the tests will be skipped). These tests run `docker run natsio/nats-box` to verify messages using the official NATS CLI tooling.

If Docker is not available locally but nats-box is accessible on your VM, the direct NATS verification tests will still run and provide coverage.

---

## Adding New Component Tests

### When to Write a Component Test

1. **New endpoint**: Every API endpoint needs at least one happy-path component test
2. **Complex NATS interaction**: Consumer creation, acknowledgment patterns
3. **Serialization**: Custom JSON formats, protobuf, binary data

### Test Structure Template

```csharp
[TestFixture]
[Category("Component")]
public class NewFeatureComponentTests : NatsComponentTestBase
{
    [Test]
    public async Task FeatureName_Scenario_ExpectedBehavior()
    {
        // Arrange
        await JetStream.CreateStreamAsync(new StreamConfig(TestStreamName, new[] { $"{TestStreamName}.>" }));

        // Act
        var response = await Client.PostAsJsonAsync($"/api/endpoint", request);

        // Assert API response
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify via direct NATS connection
        var consumer = await JetStream.CreateOrUpdateConsumerAsync(TestStreamName, config);
        var msg = await consumer.NextAsync<byte[]>();
        Assert.That(msg.HasValue, Is.True);
    }
}
```

### Best Practices

1. **Isolate tests**: Use `TestStreamName` (auto-generated unique per test)
2. **Use triangulation**: Verify via API AND direct NATS connection
3. **Test both directions**: API→NATS and NATS→API

---

## Code Coverage Strategy

### Different Goals for Different Test Types

| Metric | Unit Tests | Component Tests |
|--------|------------|-----------------|
| **Goal** | Code path coverage | Integration path coverage |
| **Target** | 80%+ line coverage | 100% endpoint coverage |
| **Focus** | All branches, edge cases | Happy paths, critical flows |

### Endpoint Coverage Matrix

| Endpoint | Tested |
|----------|:------:|
| `GET /health` | ✅ |
| `POST /api/messages/{subject}` | ✅ |
| `GET /api/messages/{subject}` | ✅ |
| `POST /streams/{stream}/consumers` | ⬜ |
| `DELETE /streams/{stream}/consumers/{name}` | ⬜ |
| `WS /ws/messages/{subject}` | ⬜ |

### When Are More Component Tests Needed?

**Red flags indicating missing coverage:**

| Symptom | Missing Test |
|---------|--------------|
| "Works in tests, fails in staging" | Serialization test |
| "Consumer isn't receiving messages" | Consumer config test |
| "Stream doesn't exist error" | Stream creation test |

**When a bug is found in UAT:**
1. Write a failing component test that reproduces it
2. Fix the bug
3. The test now guards against regression

---

## Project Structure

```
NatsHttpGateway.ComponentTests/
├── README.md
├── NatsHttpGateway.ComponentTests.csproj
├── NatsComponentTestBase.cs
├── HealthEndpointComponentTests.cs
└── MessagesEndpointComponentTests.cs
```
