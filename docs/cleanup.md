# Cleanup implementation

PoWatch now concentrates on watching the room, managing people, reviewing a day, and preparing
shift handoff. Unwired features and decorative effects were retired; core inference, BFF authentication,
server significance, evidence storage, identity merges, drift status, and handoff reports remain.
Existing working-tree inference and provider changes were preserved and incorporated.

| # | Cleanup | Implementation |
|---|---|---|
| 1 | Explicit test caps | Each test project declares `TestCaseLimit`; CI discovers all four suites and rejects over-budget suites. Theory matrices count as one behavior test only after conversion to explicit loops that execute every input. |
| 2 | Unit duplication | Adjacent scenarios for a behavior share a test, preserving assertions and setup; obsolete feature tests were deleted. |
| 3 | Integration budget | Retired endpoint tests were removed; input matrices are consolidated; durable revision-history coverage was added. |
| 4 | API E2E budget | The retired FHIR scenario was removed, retaining 25 API scenarios. |
| 5 | UI E2E budget | Duplicate navigation, layout, and appearance checks were removed; 25 interaction and viewport tests remain, including server acknowledgment. |
| 6 | Deployment consistency | Bicep and CI share `infra/deployment.json`, matching the inspected Windows host, plan, storage, and resource groups. |
| 7 | Infrastructure parameters | Dev, staging, and production parameter files use isolated application resource names; shared observability and Key Vault are existing references. |
| 8 | CI documentation | The manual now describes the actual unit/integration execution gates, four-suite cap discovery, and optional E2E execution. |
| 9 | Unwired outbox | The unused C# queue, DTO, and IndexedDB bridge were deleted. |
| 10 | Orphaned CSS | 116 unused selectors were removed from six retained stylesheets; framework classes and dynamic class families were preserved. |
| 11 | Decorative effects | Fireflies, pebbles, timeline shaders, breathing pulses, handoff beams, parallax, ripples, spatial audio, and soundscapes were removed. Functional alert/start/stop/ack cues remain. |
| 12 | Complete feature retirement | Components, CSS, JavaScript, options, contracts, repositories, source-generated JSON entries, routes, and tests were pruned together. |
| 13 | Observer complexity | Monitoring lifecycle and capture now have a dedicated partial; duplicate urgent-alert setup and unused analytics state were removed. |
| 14 | History simplicity | The duplicated pattern-comparison panel and date-restoration round trips were removed. Day navigation, narrative forms, evidence, timeline, and handoff remain. |
| 15 | System simplicity | Runtime details and hardware compatibility testing use native expandable disclosures. Storage and inference readiness stay visible. |
| 16 | Alert consolidation | The bundled banner/toast system was retired; routine observations no longer generate repeated announcements. Urgent acknowledgment calls the server, refreshes unresolved counts, prevents duplicate clicks, and retains the alert on failure. |
| 17 | Endpoint inventory | FHIR, memos, sharing, blob integrity, and the unconsumed per-person baseline endpoint were removed. Retained routes are listed below. |
| 18 | Memory-only features | Unwired voice memos and share links were retired. Identity revision history was retained with Azure Table persistence and a repository-recreation integration test. |
| 19 | Provider selection | `AiProvider:Provider` is the single Template/AzureOpenAi/Ollama choice; redundant provider flags and an unused schema option were removed. Template is the default and failure fallback. |
| 20 | Structure and naming | Helper scripts live under `SCRIPTS`, duplicate and one-off outage scripts were retired, `IdentityNexus` became `People`, the unused frame-diff asset was deleted, and the evidence image bridge was named for its purpose. CI checks source depth, names, package versions, and shell asset references. |

## Retained API surface

| Area | Routes | Consumers |
|---|---|---|
| Session | `/auth/me`, `/auth/config`, `/auth/login/microsoft`, `/auth/login/fake`, `/auth/logout` | BFF client, sign-in, sign-out, dev/test setup |
| Observation | `/api/observer/ingest`, `/api/observer/state`, `/api/observer/acknowledge` | Live Room and People acknowledgment |
| History and handoff | `/api/archives/{date}`, `/{date}/handoff-report`, `/{date}/handoff-brief` under `/api/archives` | History, handoff dialog, PDF export |
| People | `/api/identity/subjects`, `/subjects/{subjectId}`, `/subjects/{subjectId}/history`, `/merge`, `/subjects/live-status`, `/subjects/live-risk` under `/api/identity` | Registration, naming, merges, live status, revision history, drift status; history remains an authenticated API capability |
| Evidence | `/api/blobs/sas`, `/api/blobs/read` | Evidence upload/download and contract consumers |
| System | `/api/diagnostics/status`, `/api/diagnostics/reset` | System page; reset remains explicitly gated and off in production |
| Operations | `/health`, `/health/live`, `/diag`, `/diag/boot`, OpenAPI/Scalar where enabled | Health page, platform probes, deployment gate, troubleshooting |

## Verification

- Test caps: 96 unit / 100, 45 integration / 50, 25 API E2E / 25, 25 UI E2E / 25.
- All four suites passed: 96 unit, 45 integration, 25 API E2E, and 25 UI E2E. Container-backed suites use disposable Azurite storage; UI tests executed in Chromium against the isolated local server.
- Release solution builds with zero warnings and errors; formatting verification passes.
- Bicep and all three environment parameter files compile locally. Repository hygiene and discovered test-case caps pass.
- The trimmed Release API/client publish succeeds. CSS syntax and workflow YAML are validated; no retired shell assets remain.
- UI verification repaired the viewport measurement helper, confirmed mobile/tablet/desktop layout, and verifies critical acknowledgment updates server state before the alert closes.

UI tests can run against a specified `E2E_BASE_URL` or an isolated local server with `E2E_LOCAL=1`.
The local harness creates a Test-environment Kestrel host and disposable Azurite storage, uses the
template provider, and disables data reset. No production deployment or production data operation
was performed. Infrastructure templates were compiled locally; applying them is a separate deployment.
Hardware/model inference was preserved and compiled; automated UI tests do not download model weights
or certify camera/WebGPU performance.
