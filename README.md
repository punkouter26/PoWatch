# PoWatch

Clinical observation monitoring system that ingests, archives, and analyzes activity events from a monitored environment (e.g., a healthcare facility room). Built on **.NET 10 / Blazor WASM** with **Onion Architecture** and **Azure Table/Blob Storage**.

---

## Quick Start

```bash
# Start Azurite storage emulator
docker compose up -d azurite

# Run the API
dotnet run --project src/PoWatch.Api

# Tests
dotnet test tests/PoWatch.UnitTests
dotnet test tests/PoWatch.IntegrationTests
cd tests/PoWatch.E2E && npm test
```

**API**: `http://localhost:5000` | **Scalar UI**: `http://localhost:5000/scalar/v1` | **Health**: `http://localhost:5000/health`

---

## Feature Flags (`appsettings.json`)

| Flag | Default | Purpose |
|---|---|---|
| `ObservationLoopEnabled` | `true` | Gates all observation ingestion |
| `SaveSignificantImages` | `true` | Generates blob SAS references for significant events |
| `TtsAnnouncementsEnabled` | `false` | Placeholder for future audio announcements |
| `ExposeDebugDetailsInUi` | `false` | Exposes exception details in error responses |
| `DeveloperBypassAuth` | `false` | Enables FakeAuth dev scheme (dev only) |

---

## Architecture

**Onion Architecture** — dependency direction: `Api → Application → Domain` (Infrastructure implements Application contracts)

| Layer | Project | Responsibility |
|---|---|---|
| Domain | `PoWatch.Domain` | `ObservationEvent`, `SubjectProfile`, `ClinicalTagParser`, `SubjectIdSlugger` — zero external deps |
| Application | `PoWatch.Application` | `ObservationService`, `ArchivesService`, `IdentityService` + repository/provider contracts |
| Infrastructure | `PoWatch.Infrastructure` | Azure Table/Blob + in-memory repository implementations |
| API | `PoWatch.Api` | Minimal API endpoints, middleware, health checks, OpenTelemetry |
| Client | `PoWatch.Client` | Blazor WASM SPA — ObserverHub, Archives, IdentityNexus, Diagnostics |
| Shared | `PoWatch.Shared` | DTO contracts used by both Client and API |

---

## Documentation — `/docs` Folder

### Master Mermaid Suite

#### 1. Architecture & CI/CD Strategy

| File | Description |
|---|---|
| [docs/Architecture_MASTER.mmd](docs/Architecture_MASTER.mmd) | Hybrid C4 Level 1/2 — Edge, Compute, Data tiers + external services |
| [docs/Architecture_MASTER_SIMPLE.mmd](docs/Architecture_MASTER_SIMPLE.mmd) | Simplified architecture for stakeholder review |
| [docs/ReleasePipeline_MASTER.mmd](docs/ReleasePipeline_MASTER.mmd) | CI/CD pipeline: Build → Unit → Integration → E2E → Dev → Staging → Prod |
| [docs/ReleasePipeline_MASTER_SIMPLE.mmd](docs/ReleasePipeline_MASTER_SIMPLE.mmd) | Simplified pipeline flow |

#### 2. User Usage & Behavioral Flowcharts

| File | Description |
|---|---|
| [docs/OnboardingJourney.mmd](docs/OnboardingJourney.mmd) | New user path: access → auth → ObserverHub → first observation → Aha! moment |
| [docs/OnboardingJourney_SIMPLE.mmd](docs/OnboardingJourney_SIMPLE.mmd) | Simplified onboarding flow |
| [docs/PrimaryValueFlow.mmd](docs/PrimaryValueFlow.mmd) | Happy path: sensor → ingest → storage → archives → UI render |
| [docs/PrimaryValueFlow_SIMPLE.mmd](docs/PrimaryValueFlow_SIMPLE.mmd) | Simplified primary value flow |
| [docs/ExceptionUserFlows.mmd](docs/ExceptionUserFlows.mmd) | Backpressure drops, loop disabled, outlier flagging, auth failures, identity validation errors |
| [docs/ExceptionUserFlows_SIMPLE.mmd](docs/ExceptionUserFlows_SIMPLE.mmd) | Simplified exception flows |

#### 3. Logic & State Dynamics

| File | Description |
|---|---|
| [docs/SystemFlow_MASTER.mmd](docs/SystemFlow_MASTER.mmd) | Full system flow: auth middleware → all four pipelines (observer, archives, identity, diagnostics) |
| [docs/SystemFlow_MASTER_SIMPLE.mmd](docs/SystemFlow_MASTER_SIMPLE.mmd) | Simplified system flow |
| [docs/StateDynamics_MASTER.mmd](docs/StateDynamics_MASTER.mmd) | `stateDiagram-v2` — ObservationEvent lifecycle, Subject identity lifecycle, ProcessingGate states |
| [docs/StateDynamics_MASTER_SIMPLE.mmd](docs/StateDynamics_MASTER_SIMPLE.mmd) | Simplified state dynamics |

#### 4. Data & Security Schema

| File | Description |
|---|---|
| [docs/DataModel.mmd](docs/DataModel.mmd) | ERD — `ObservationEvent`, `SubjectProfile`, `DailyChapter`, `Highlight`, `BlobAccessDescriptor`, `DiagnosticsSnapshot`, `FeatureFlags`, `AzureStorageOptions` |
| [docs/DataModel_SIMPLE.mmd](docs/DataModel_SIMPLE.mmd) | Simplified entity relationships |
| [docs/AccessControl_MATRIX.mmd](docs/AccessControl_MATRIX.mmd) | Role-to-endpoint matrix — anonymous vs dev-authenticated, security notes, module ownership |
| [docs/AccessControl_MATRIX_SIMPLE.mmd](docs/AccessControl_MATRIX_SIMPLE.mmd) | Simplified access control |
| [docs/DataLifecycle_MASTER.mmd](docs/DataLifecycle_MASTER.mmd) | High-density trace: Ingestion → Processing → Persistence → Transformation → Egress |
| [docs/DataLifecycle_MASTER_SIMPLE.mmd](docs/DataLifecycle_MASTER_SIMPLE.mmd) | Simplified data lifecycle |

#### 5. Dependency & UI Hierarchy

| File | Description |
|---|---|
| [docs/SystemInteractionFlow.mmd](docs/SystemInteractionFlow.mmd) | Sequence diagram — concurrent ingest + poll, archives retrieval, identity rename with row-key rewriting |
| [docs/SystemInteractionFlow_SIMPLE.mmd](docs/SystemInteractionFlow_SIMPLE.mmd) | Simplified interaction flow |
| [docs/ServiceMap_MASTER.mmd](docs/ServiceMap_MASTER.mmd) | Full dependency graph — solution projects, DI contracts, implementations, external services, test projects |
| [docs/ServiceMap_MASTER_SIMPLE.mmd](docs/ServiceMap_MASTER_SIMPLE.mmd) | Simplified service map |
| [docs/InterfaceHierarchy_MASTER.mmd](docs/InterfaceHierarchy_MASTER.mmd) | Blazor component tree, `PoWatchApiClient` methods, `ClientFeatureFlagsOptions`, DTO mapping |
| [docs/InterfaceHierarchy_MASTER_SIMPLE.mmd](docs/InterfaceHierarchy_MASTER_SIMPLE.mmd) | Simplified interface hierarchy |

---

## Refactor Blast Radius Assessment

> **Before any refactor, consult [docs/ServiceMap_MASTER.mmd](docs/ServiceMap_MASTER.mmd) as the source of truth.**

### Critical Dependency Analysis

| Change Target | Downstream Impact | Risk Level |
|---|---|---|
| `IObservationRepository` contract | `AzureObservationRepository`, `InMemoryObservationRepository`, `ObservationService`, `ArchivesService`, `IdentityService`, all integration + unit tests | **HIGH** |
| `ISubjectRepository` contract | `AzureSubjectRepository`, `InMemorySubjectRepository`, `ObservationService`, `IdentityService` | **HIGH** |
| `ObservationEvent` model | `PoWatch.Domain`, `PoWatch.Infrastructure` (row key format, table entity), `PoWatch.Application`, `PoWatch.Shared` (DTOs), `PoWatch.Client`, all tests | **CRITICAL** |
| `DailyChapterDto` / `ObservationEventDto` | `PoWatch.Shared`, `PoWatch.Client` (Archives.razor), `PoWatch.Api` (ArchivesEndpoints), integration + E2E tests | **HIGH** |
| `FeatureFlagsOptions` | `PoWatch.Api` (Program.cs, all endpoints), `PoWatch.Application` (ObservationService), `PoWatch.Client` (ClientFeatureFlagsOptions) | **MEDIUM** |
| `AzureStorageClients` | All Azure repository implementations and `AzureBlobSasProvider` — switching storage SDK version | **HIGH** |
| Auth scheme (FakeAuth) | `PoWatch.Api/Security/`, all integration tests using `AzuriteWebApplicationFactory`, E2E tests | **MEDIUM** |

### Service Map Summary

```
Client (Blazor WASM)
    ↓ HttpClient REST calls
PoWatch.Api (Minimal API)
    ↓ Business Logic
PoWatch.Application (Services + Contracts)
    ↓ Dependency Inversion
PoWatch.Infrastructure (Implementations)
    ↓ Azure SDK
Azure Table Storage + Azure Blob Storage
```

### Test Coverage Blast Radius

| Test Project | Tests | Dependencies |
|---|---|---|
| `PoWatch.UnitTests` | Domain logic, ClinicalTagParser, service logic | Domain, Application (no I/O) |
| `PoWatch.IntegrationTests` | Full API pipeline | API, Infrastructure, Azurite via Testcontainers |
| `PoWatch.E2E` | Browser automation | Client, API (full stack) |

---

## Key Data Flows

```
Sensor POST → Gate (semaphore) → Subject resolve → Clinical parse → Redundancy check → Table write → Subject cache update
GET /archives/{date} → Partition query → Sort ASC → Highlight filter (top 30) → Narrative build → DailyChapterDto
PATCH /identity/subjects/{id} → Rename subject → MergeSubjectAsync (rebuild all row keys) → old profile deleted
```

---

## Storage Schema

| Table | Partition Key | Row Key | Notes |
|---|---|---|---|
| `PoWatchObservations` | `yyyyMMdd` | `{SubjectId}_{InvertedTicks:D19}_{EventId:N}` | Reverse-chrono order within subject |
| `PoWatchSubjects` | `"Subjects"` | `{SubjectId}` | `LastActivity` cached for O(1) redundancy check |

---

## Tech Stack

- **.NET 10** — `global.json` SDK `10.0.201`
- **Blazor WASM** + **Radzen.Blazor 10.2.3**
- **Azure.Data.Tables 12.9.0** + **Azure.Storage.Blobs 12.22.0**
- **Serilog 9.0.0** — console + file (`logs/powatch-.log`) + Application Insights sinks
- **OpenTelemetry 1.12.0** — ASP.NET Core + custom `PoWatch.Storage` ActivitySource
- **Scalar.AspNetCore 2.13.22** — interactive API docs at `/scalar/v1`
- **xUnit 2.9.3** + **Testcontainers.Azurite 4.11.0** — unit + integration testing
- **Playwright** (TypeScript) — E2E testing

---

## Mermaid Syntax Standards

All diagrams in `/docs` follow these strict conventions:

1. **Direct Starts**: Files begin immediately with `flowchart`, `erDiagram`, `sequenceDiagram`, `stateDiagram-v2`, or `C4Context`
2. **Grouping**: Every logical boundary uses `subgraph Name["Label"]`
3. **Quoted Labels**: All labels containing spaces or special characters are double-quoted
4. **Total Styling**: Every style line explicitly specifies `fill`, `stroke`, and `color`
5. **Contrast Rules**: Dark fill with `color:#fff` or light fill with `color:#000` for legibility

### Diagram Types Used

| Type | Purpose | Files |
|---|---|---|
| `C4Context` | Architecture visualization | Architecture_MASTER, Architecture_MASTER_SIMPLE |
| `flowchart TD/LR` | Process flows, hierarchies, maps | All other MASTER files |
| `erDiagram` | Entity relationships | DataModel, DataModel_SIMPLE |
| `stateDiagram-v2` | Entity lifecycles | StateDynamics_MASTER, StateDynamics_MASTER_SIMPLE |
| `sequenceDiagram` | Interaction sequences | SystemInteractionFlow, SystemInteractionFlow_SIMPLE |
