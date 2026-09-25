# PoWatch — Capability Map

Maps every capability to the project that owns it, and records what each caregiver-era subsystem
became. `SPEC.md` is the source of truth for behavior; this file is the source of truth for *where*
things live. Synced with the code at the end of task G4.

## Modules

| Project | Owns | Must not |
|---|---|---|
| `PoWatch.Domain` | Pure models and math: `Session`, `Tick`, `SceneEvent`, `Rollup` (+ `RollupBuckets`), `Regular`; calculators `PresenceStats`, `SpaceStats`, `PatternStats`, `AnomalyMath`, `EnvironmentStats`, `CaptionStats`, `AchievementRules`, `RecordRules`, `RegularMatcher`, `LocalDay` | Reference any other project, I/O, clocks (timestamps come in as input) |
| `PoWatch.Application` | Use cases `SessionService`, `IngestService`, `StatsQueryService`, `RegularsService`, `RecapService` (+ `TemplateRecap`), `AchievementService`; store contracts; options | Reference Api or Infrastructure |
| `PoWatch.Infrastructure` | Azure Table/Blob stores and in-memory fallbacks (`AzureSensingStores`, `AzureStatsStores`, `RegularStores`, `SnapshotStores`), recap AI wiring (`RecapAi`), storage initializer, diagnostics provider | Business rules beyond persistence mapping |
| `PoWatch.Api` | Minimal API vertical slices (`Features/*`), SignalR `StatsHub`, BFF auth, recap PDF rendering, middleware, telemetry; hosts the WASM client | Stat math (delegates to Application/Domain) |
| `PoWatch.Shared` | Cross-boundary DTOs; trim-safe client/server helpers: `CentroidTracker`, `TickBatcher`, `CaptionParser`, `VlmScheduler`, `HighlightRules`, `SyntheticScene` | Server-only or browser-only APIs; must stay trim-clean |
| `PoWatch.Client` | Blazor WASM UI (terminal theme, Radzen), `SensingSession` orchestration, sensing JS (pixel layer, detector worker, VLM worker), outbox and time-lapse in IndexedDB | Persist anything authoritative; call storage directly |

## Capabilities

| # | Capability | Stat family | Client | Shared | Api | Application | Domain | Infrastructure |
|---|---|---|---|---|---|---|---|---|
| C1 | Sessions (start/stop, "while you were away") | core | Live, `AwayCard` | `SessionDto` | `Features/Sessions` | `SessionService` | `Session` | `AzureSessionRepository` |
| C2 | Pixel layer (motion, luminance, palette, 16×9 grid) | A, D | `js/sensing/pixel-layer.js` | `TickDto` | — | — | — | — |
| C3 | Detector layer (boxes, tracks, entry/exit, look signatures) | A, B | `js/sensing/detector-worker.js` | `CentroidTracker` | — | — | — | — |
| C4 | VLM layer (captions, adaptive cadence) | D | `js/inference-worker.js` | `CaptionParser`, `VlmScheduler` | — | — | — | — |
| C5 | Tick ingest (10 s ticks + scene events, idempotent by batch key) | all | `TickBatcher`, `js/outbox.js` | `IngestBatchDto` | `Features/Ingest` | `IngestService` | validation | `AzureSensingLog`, `AzureIngestLedger` |
| C6 | Rollups (minute / hour / day / all-time) | A–D | — | — | — | `IngestService` | `Rollup` merge | `AzureRollupStore` |
| C7 | Stats queries (session / today / 7d / 30d / all / day) | A–D, F | Stats tabs | `StatsDtos` | `Features/Stats` (HybridCache) | `StatsQueryService` | calculators | rollup reads |
| C8 | Regulars (recognised by look, named, merged) | B | `/regulars`, `NamePrompts` | `RegularDtos` | `Features/Regulars` | `RegularsService` | `Regular`, `RegularMatcher` | `AzureRegularStore` |
| C9 | Anomaly ("today vs usual") | C | Stats → Patterns | `AnomalyDto` | `Features/Stats` | `StatsQueryService` | `AnomalyMath` | rollup reads |
| C10 | Highlight snapshots + moments | A, B | `SensingSession`, `js/blob-upload.js` | `HighlightRules`, `MomentDto` | `Features/Snapshots` | `RecapService.MomentsAsync` | — | `AzureSnapshotStore` |
| C11 | Recaps + PDF (session / day) | D | History recap panel, `AwayCard` PDF link | `RecapDto` | `Features/Recaps`, `RecapReportRenderer` | `RecapService`, `TemplateRecap` | — | `RecapAi` (Ollama / Azure OpenAI) |
| C12 | Achievements & records | E | `/trophies`, `TrophyToasts` | `AchievementDtos` | `Features/Achievements` | `AchievementService` | `AchievementRules`, `RecordRules` | `AzureAchievementStore` |
| C13 | Pipeline telemetry | F | `/system`, Stats → Pipeline | `PipelineStatsDto` | `Features/Diagnostics`, `Features/Stats` | `StatsQueryService` | — | `LocalDiagnosticsProvider` |
| C14 | Stats wall | all | `/display` (MainLayout wall mode) | reuses stats DTOs | reuses Stats | — | — | — |
| C15 | Live push | all | `StatsFeed` | `StatsChangedDto`, `AchievementsUnlockedDto` | `StatsHub` | — | — | — |
| C16 | Auth (BFF cookie, Entra ID / guest) | — | `BffAuthenticationStateProvider` | `AuthDtos` | `Features/Auth`, `Security/*` | — | — | Data Protection |
| C17 | Health & operations | — | `/health` | `HealthDtos` | health checks, `/diag` | — | — | `StartupReadiness` |
| C18 | Time-lapse (on-device only) | — | History, `js/timelapse.js` | — | — | — | — | — |

## Subsystem disposition (caregiver → stat cam) — done

| Old subsystem | Disposition | Became |
|---|---|---|
| Observer Hub + VLM inference loop | Refactored | Live page with 3-layer sensing (C2–C4) |
| Adaptive cadence (frame diff) | Retained | Drives VLM cadence (`VlmScheduler`) |
| Idempotency middleware | Replaced | Batch-key ledger (`IIngestLedger`) |
| People / Identity (subjects, merge, revisions) | Replaced | Regulars (C8) |
| Archives / daily chapter | Replaced | History day view + Stats (C7) |
| Handoff report + PDF + summarizers | Replaced | Session/day recaps (C11) |
| Drift radar | Replaced | Anomaly stats (C9) |
| Evidence blobs (SAS) | Replaced | Highlight snapshots (C10) |
| `/display` kiosk | Refactored | Stats wall (C14) |
| Diagnostics / System / Health | Retained | System page (data reset removed) |
| BFF auth, telemetry, Key Vault, Data Protection | Retained | unchanged |
| Significance classifier | Replaced | `HighlightRules` + caption "notable" flag |
| Urgent alerts, threshold rules, acknowledgment, clinical parser, shift clock, audio cues | Pruned | — |
