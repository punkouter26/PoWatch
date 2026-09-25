# PoWatch — Capability Map

Maps every capability of the stat-cam pivot to the project that owns it, and records what each
caregiver-era subsystem becomes. `SPEC.md` is the source of truth for behavior; this file is the
source of truth for *where* things live.

## Modules

| Project | Owns | Must not |
|---|---|---|
| `PoWatch.Domain` | Pure models and pure math: `Session`, `Tick`, `SceneEvent`, `Regular`, `Rollup`, `Achievement`, stat calculators (dwell, z-score, streaks, rhythm score) | Reference any other project, I/O, clocks (take `TimeProvider`/timestamps as input) |
| `PoWatch.Application` | Use-case services (`SessionService`, `IngestService`, `RollupService`, `StatsQueryService`, `RegularsService`, `AnomalyService`, `AchievementService`, `RecapService`), repository contracts, options | Reference Api/Infrastructure/Shared DTOs |
| `PoWatch.Infrastructure` | Azure Table/Blob repositories, in-memory fallbacks, recap summarizers (Template/AzureOpenAi/Ollama), diagnostics provider | Business rules beyond persistence mapping |
| `PoWatch.Api` | Minimal API vertical slices (`Features/*`), BFF auth, PDF rendering, middleware, telemetry, hosts the WASM client | Stat math (delegates to Application/Domain) |
| `PoWatch.Shared` | Cross-boundary DTOs, source-gen JSON context inputs, client/server-shared pure helpers (cadence, heatmap builders) | Server-only or browser-only APIs; must stay trim-clean |
| `PoWatch.Client` | Blazor WASM UI (Radzen), camera capture, sensing pipeline JS (pixel layer, detector, VLM worker), client-side tick batching | Persist anything authoritative; call storage directly |

## Capabilities

| # | Capability | Stat family | Client | Shared | Api | Application | Domain | Infrastructure |
|---|---|---|---|---|---|---|---|---|
| C1 | Sessions (start/stop/resume, "while you were away") | core | Live page, session controls | `SessionDto` | `Features/Sessions` | `SessionService` | `Session` | `AzureSessionRepository` |
| C2 | Pixel layer (motion, luminance, palette, motion grid) | A, D | `js/sensing/pixel-layer.js` | `TickDto` | — | — | — | — |
| C3 | Detector layer (objects, boxes, tracks, entry/exit) | A, B | `js/sensing/detector-worker.js`, tracker | `TickDto`, `DetectionDto` | — | — | `Track` math (IoU) | — |
| C4 | VLM layer (captions + structured scene JSON) | D | existing `inference-worker.js` (re-prompted) | `CaptionDto` | — | — | caption parsing | — |
| C5 | Tick ingest (batched 10 s ticks + discrete events) | all | `TickBatcher` | `IngestBatchDto` | `Features/Ingest` | `IngestService` | validation | `AzureTickRepository`, `AzureEventRepository` |
| C6 | Rollups (minute / hour / day / all-time) | A–D | — | — | — | `RollupService` | `Rollup` merge math | `AzureRollupRepository` |
| C7 | Stats queries (range: session / day / week / month / all-time) | A–D, F | Stats page | `StatsDto` family | `Features/Stats` | `StatsQueryService` | calculators | rollup reads |
| C8 | Regulars (any recurring entity; name, merge, history) | B | Regulars page | `RegularDto` | `Features/Regulars` (was Identity) | `RegularsService` (was IdentityService) | `Regular` (was SubjectProfile) | Azure regular + revision repos |
| C9 | Anomaly ("today vs usual") | C | Stats → Patterns tab | `AnomalyDto` | `Features/Stats` | `AnomalyService` (was DriftRadarService) | `DriftMath` → z-score | rollup reads |
| C10 | Highlight snapshots | A, B, E | snapshot picker, gallery | `SnapshotDto` | `Features/Snapshots` (was Blobs) | `SnapshotService` | highlight rules | `AzureBlobSasProvider` |
| C11 | Recaps + PDF (session / day) | D | Recap dialog | `RecapDto` | `Features/Recaps`, `RecapReportRenderer` (was HandoffReportRenderer) | `RecapService` (was HandoffCoachService/ReportService) | — | summarizers (renamed) |
| C12 | Achievements & records | E | Trophies page, unlock toast | `AchievementDto`, `RecordDto` | `Features/Achievements` | `AchievementService` | achievement rules | `AzureAchievementRepository` |
| C13 | Nerd telemetry | F | System page + Stats → Pipeline tab | `PipelineStatsDto` | `Features/Diagnostics` | — | — | `LocalDiagnosticsProvider` |
| C14 | Stats wall | all | `/display` | reuses `StatsDto` | reuses Stats | — | — | — |
| C15 | Auth (BFF cookie, Entra ID / guest) | — | `BffAuthenticationStateProvider` | `AuthDtos` | `Features/Auth` | — | — | Data Protection |
| C16 | Health & operations | — | Health page | `HealthDtos` | health checks, `/diag` | — | — | readiness |

## Subsystem disposition (caregiver → stat cam)

| Existing subsystem | Disposition | Becomes |
|---|---|---|
| Observer Hub + VLM inference loop | **Refactor** | Live page with 3-layer sensing (C2–C4) |
| Adaptive cadence (frame diff) | **Retain** | Drives VLM cadence; pixel layer supplies the diff |
| Idempotency middleware | **Retain** | Guards batch ingest retries |
| People / Identity (subjects, merge, revisions) | **Refactor** | Regulars (C8) |
| Archives / daily chapter / heatmap | **Refactor** | Day view inside History + Stats (C7) |
| Handoff report + PDF + summarizers | **Refactor** | Session/day recaps (C11) |
| Drift radar | **Refactor** | Anomaly stats (C9) |
| Evidence blobs (SAS) | **Refactor** | Highlight snapshots (C10) |
| `/display` kiosk | **Refactor** | Stats wall (C14) |
| Diagnostics / System / Health | **Retain + expand** | Adds nerd telemetry (C13) |
| BFF auth, telemetry, Key Vault, Data Protection | **Retain** | unchanged |
| Significance classifier | **Refactor** | "Notable moment" score feeding highlights + achievements |
| Urgent alerts, threshold rules, acknowledgment | **Prune** | — |
| Clinical tag parser | **Prune** | Replaced by structured VLM JSON parsing (C4) |
| Shift clock / caregiver local day | **Prune** | Plain user local day + sessions |
| Alert audio cues (`audio-bridge.js`) | **Prune** | — |
