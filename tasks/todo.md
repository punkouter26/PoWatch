# PoWatch — Task Checklist

Execute in order. See `tasks/plan.md` for architecture, risks and checkpoints.

Rules for every task:
- At most 5 files touched. Deletions of files wholly owned by a removed feature, and the F1 mechanical
  rename, are exempt.
- Order: Red → Green → run the related tests → Build Release → restart the app and confirm `/health`
  → one local commit (casual message + Co-Authored-By).
- `V:` is the verification command.

### A — Prune caregiver features
- [ ] **A0** Housekeeping.
  - Install ponytail.
  - Commit the `AGENT.md`/`AUDIT.md`/`CLAUDE.md` deletions and the new docs.
  - Point README at `AGENTS.md` and `SPEC.md`.
  - Files: README.md, docs/README.md.
  - AC: README has no reference to `AGENT.md`. V: `git status` is clean.
- [ ] **A1** Remove urgent alerts and threshold rules.
  - Delete `AlertThresholdEvaluator`, `AlertThresholdOptions`, `AlertThresholdRule`,
    `ThresholdAlertBanner`, `RoomAlertOverlay` and their tests.
  - Edit `ObservationService.cs`, `Application/DependencyInjection.cs`, `Program.cs`,
    `appsettings.json`, `ObserverDtos.cs`.
  - AC: no `ThresholdAlert` or `AlertLevel` symbols remain; the Live page runs.
  - V: `dotnet test tests/PoWatch.Unit --filter Observation`
- [ ] **A2** Remove acknowledgment.
  - Delete `IAcknowledgementRegistry`, `InMemoryAcknowledgementRegistry`, `LiveDashboardDtos` (ack
    parts).
  - Edit `ObserverEndpoints.cs`, `IdentityService.cs`, `PoWatchApiClient.cs`, `PoWatchJsonContext.cs`,
    `ObserverHub.State.razor.cs`.
  - AC: `/api/observer/acknowledge` returns 404.
  - V: `dotnet test tests/PoWatch.Integration --filter EndpointContract`
- [ ] **A3** Remove the clinical parser, outliers and audio cues.
  - Delete `ClinicalTagParser`, `audio-bridge.js` and their tests.
  - Edit `ObservationService.cs`, `ObserverHub.Monitoring.razor.cs`, `ObserverHub.razor.cs`,
    `index.html`, `inference-bridge.js` (`speakBedsideCue`).
  - AC: no `Clinical`/`powatchAudio` references in the client JS/Razor.
  - V: unit tests + `check-hygiene.ps1`
- [ ] **A4** Replace `ShiftClock` with `LocalDay` (tz-aware helper in Domain).
  - Files: `ShiftClock.cs` → `Domain/Services/LocalDay.cs`, `ArchivesService.cs`, `IdentityService.cs`,
    `ReportService.cs`, `ShiftWindowTests.cs` → `LocalDayTests.cs`.
  - AC: a midnight-crossing test passes.
  - V: `--filter LocalDay` · **CHECKPOINT**

### B — Domain core (pure, TDD)
- [ ] **B1** Models `Session`, `Tick`, `SceneEvent` (+ kinds), with invariants.
  - Files: `Domain/Models/{Session,Tick,SceneEvent}.cs`, unit test.
  - AC: invalid ticks (negative counts, future > 5 min) are rejected.
- [ ] **B2** `Rollup` with Merge and grains, using Tensors.
  - Files: `Domain/Models/Rollup.cs`, `Domain/Services/RollupMath.cs`, `RollupTests.cs` (CsCheck
    associativity + identity), props.
  - AC: property test passes over 10k samples.
- [ ] **B3** Stats family A calculators: occupancy, dwell p50/p90/max, visits/h, peak concurrency,
  empty streak, busiest minute, stillness streak, entry/exit edges.
  - Files: `Domain/Services/PresenceStats.cs`, `SpaceStats.cs`, 2 test files.
  - AC: hand-computed fixtures match.
  - **CHECKPOINT**
- [ ] **B4** Family C calculators: hour×weekday, rhythm-score correlation, z-scores (|z| ≥ 2),
  7/30-day trend slope, busy-hour forecast. Replaces `DriftMath`.
  - Files: `PatternStats.cs`, `AnomalyMath.cs`, delete `DriftMath.cs`, tests.
- [ ] **B5** Family D calculators: light curve, lights on/off steps, sunrise/sunset estimate, palette
  merge, word frequencies, weirdest caption (Jaccard).
  - Files: `EnvironmentStats.cs`, `CaptionStats.cs`, tests.
- [ ] **B6** Achievements (≥ 15 definitions) and records; evaluation is idempotent.
  - Files: `Domain/Models/Achievement.cs`, `Domain/Services/AchievementRules.cs`, `RecordRules.cs`,
    tests.
  - AC: the same input twice unlocks once. **CHECKPOINT**

### C — Storage, ingest, queries
- [ ] **C1** Contracts plus in-memory repos: Session, Tick, SceneEvent, Rollup, AllTime, Achievement.
  - Files: `Application/Contracts/*` (2 files grouping the interfaces),
    `Infrastructure/Persistence/InMemoryStatsStores.cs`, DI.
- [ ] **C2** Azure repos for Sessions, Ticks, Events; initializer tables.
  - AC: Azurite round trip; keys per SPEC §6.
  - V: `dotnet test tests/PoWatch.Integration --filter Repo`
- [ ] **C3** Azure rollup + AllTime repo with ETag merge and retry.
  - AC: a concurrent-merge integration test passes. **CHECKPOINT**
- [ ] **C4** `SessionService` + `/api/sessions` (start with tz, stop, list, get) + DTOs + JSON ctx.
  - AC: API E2E session lifecycle passes.
- [ ] **C5** `IngestService` + `POST /api/sessions/{id}/batches` (FluentValidation; idempotency
  extended to `batchKey`; writes ticks and events, merges rollups; HybridCache tag eviction).
  - AC: replaying ×3 gives identical rollups; a future tick gets 400.
- [ ] **C6** `StatsQueryService` + `/api/stats/{family}?range=` (A–D, F) + the dev-only Bogus seed
  endpoint.
  - AC: Today ≤ 1.5 s and all-time over 365 seeded days ≤ 2 s (API E2E timing). **CHECKPOINT**

### D — Client sensing
- [ ] **D0 (spike, no commit unless adopted)** Background-tab behavior: worker timer +
  `requestVideoFrameCallback` + Wake Lock in Chrome/Edge. Findings are recorded in `tasks/plan.md`.
- [ ] **D1** L0 pixel layer: `wwwroot/js/sensing/pixel-layer.js` (luminance, 5-color palette, 16×9
  grid, reusing `computeFrameDiff`), a bridge export, and a `PixelSample` DTO.
  - AC: the System page shows L0 Hz ≥ 4.
- [ ] **D2** L1 detector: `wwwroot/js/sensing/detector-worker.js` (RF-DETR Nano, fallback
  RT-DETRv2) + `Shared/Services/Tracking/CentroidTracker.cs` (IoU match, 3 s gap merge, enter/exit edge)
  + tracker tests.
- [ ] **D3** `Shared/Services/Sensing/TickBatcher.cs` (10 s fold, 1 h replay queue, batch keys) +
  FakeTimeProvider tests + an ApiClient method.
- [ ] **D4** L2 VLM: a structured JSON prompt, `Shared/Services/Sensing/CaptionParser.cs` (tolerant
  JSON), and **wire in `AdaptiveCadence`** (replacing the fixed delay).
- [ ] **D5** `SyntheticSampleSource` (seeded, trim-safe) behind `MockInferenceService` / Demo mode.
  - AC: a fresh clone with no camera shows nonzero counters within 15 s. **CHECKPOINT**

### Phase 3 gate — `/design`, 10 concepts, you pick. Then the component hierarchy is confirmed.

### E — UI (Radzen first, mission-control theme)
- [ ] **E1** Theme tokens (dark default, `--rz-*` mapping, monospace numerals) + new nav (Live, Stats,
  History, Regulars, Trophies, System).
- [ ] **E2** Live page rewrite: overlay canvas (boxes + motion heat), live counters and sparklines,
  Start/Stop session, Wake Lock.
- [ ] **E3** `/stats` shell + range picker + Presence & Motion + Space tabs.
- [ ] **E4** Objects & Regulars + Patterns & Anomalies tabs (removes `DriftDetailPanel`).
- [ ] **E5** Environment & Captions + Pipeline tabs. **CHECKPOINT**
- [ ] **E6** SignalR `StatsHub` + client subscription (trim-clean build first, then feature).
- [ ] **E7** `/display` stats wall (no scroll at 1920×1080 and 1280×720, ≤ 5 s refresh).
- [ ] **E8** "While you were away" card + highlight snapshots (`SnapshotService`, highlight rules,
  200/day cap, reuse of `blob-upload.js`).
- [ ] **E9** `/history`: calendar heatmap → day → sessions (replaces the Archives page).
  **CHECKPOINT**

### F — Regulars, recaps, achievements
- [ ] **F1** Mechanical rename Subject → Regular, `/api/identity` → `/api/regulars` (no behavior
  change; exempt from 5 files).
- [ ] **F2** Track → regular matching (class + color signature + size) + co-occurrence stats.
- [ ] **F3** `RecapService` on M.E.AI (`IChatClient`: Azure OpenAI / Ollama) + `TemplateRecap`
  fallback + Humanizer + Verify snapshot. Replaces `HandoffCoachService`/`ReportService`.
- [ ] **F4** `RecapReportRenderer` (QuestPDF) + session/day PDF endpoints.
  - AC: a valid PDF (API E2E).
- [ ] **F5** `AchievementService` + endpoints + `/trophies` + unlock toast. **CHECKPOINT**

### G — Cleanup and verification prep
- [ ] **G1** Retire the old `/api/observer/ingest`, `ObservationService`, archives/handoff endpoints and
  the old table names. The initializer only creates the new tables.
- [ ] **G2** `check-hygiene.ps1`: banned-words check (caregiver, clinical, handoff, shift, nurse,
  patient, acknowledge).
- [ ] **G3** `tests/PoWatch.Benchmarks` (BenchmarkDotNet, excluded from the cap script) for rollup
  merge and stats queries.
- [ ] **G4** Docs: README, `docs/` refresh, retire `docs/cleanup.md`; sync `SPEC.md`. **CHECKPOINT**
- [ ] **G5 (gated deploy)** Push = production deploy. Before pushing: list the old tables and
  containers, get explicit confirmation, delete them, then push and verify the prod `/health`.

Then Phase 5: full suite, `/code-review`, `/security-review`, `/simplify`, `/ponytail-review`, and
evidence for SPEC §15 criteria 1–15.
