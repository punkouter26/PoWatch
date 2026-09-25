# PoWatch — Task Checklist

Execute in order. See `tasks/plan.md` for architecture, risks and checkpoints.

Rules for every task:
- At most 5 files touched. Deletions of files wholly owned by a removed feature, and the F1 mechanical
  rename, are exempt.
- Order: Red → Green → run the related tests → Build Release → restart the app and confirm `/health`
  → one local commit (casual message + Co-Authored-By).
- `V:` is the verification command.

### A — Prune caregiver features
- [x] **A0** Housekeeping.
  - Install ponytail.
  - Commit the `AGENT.md`/`AUDIT.md`/`CLAUDE.md` deletions and the new docs.
  - Point README at `AGENTS.md` and `SPEC.md`.
  - Files: README.md, docs/README.md.
  - AC: README has no reference to `AGENT.md`. V: `git status` is clean.
- [x] **A1** Remove urgent alerts and threshold rules.
  - Delete `AlertThresholdEvaluator`, `AlertThresholdOptions`, `AlertThresholdRule`,
    `ThresholdAlertBanner`, `RoomAlertOverlay` and their tests.
  - Edit `ObservationService.cs`, `Application/DependencyInjection.cs`, `Program.cs`,
    `appsettings.json`, `ObserverDtos.cs`.
  - AC: no `ThresholdAlert` symbols remain; the Live page runs. (`AlertLevel` went with the urgent
    overlay in A2.)
  - V: `dotnet test tests/PoWatch.Unit --filter Observation`
- [x] **A2** Remove acknowledgment.
  - Delete `IAcknowledgementRegistry`, `InMemoryAcknowledgementRegistry`, `LiveDashboardDtos` (ack
    parts).
  - Edit `ObserverEndpoints.cs`, `IdentityService.cs`, `PoWatchApiClient.cs`, `PoWatchJsonContext.cs`,
    `ObserverHub.State.razor.cs`.
  - AC: `/api/observer/acknowledge` returns 404.
  - V: `dotnet test tests/PoWatch.Integration --filter EndpointContract`
- [x] **A3** Remove the clinical parser, outliers and audio cues.
  - Delete `ClinicalTagParser`, `audio-bridge.js` and their tests.
  - Edit `ObservationService.cs`, `ObserverHub.Monitoring.razor.cs`, `ObserverHub.razor.cs`,
    `index.html`, `inference-bridge.js` (`speakBedsideCue`).
  - AC: no `powatchAudio` references in the client. The `Clinical*` field names on the old ingest DTO
    stay until G1 retires that path.
  - V: unit tests + `check-hygiene.ps1`
- [x] **A4** Replace `ShiftClock` with `LocalDay` (tz-aware helper in Domain).
  - Files: `ShiftClock.cs` → `Domain/Services/LocalDay.cs`, `ArchivesService.cs`, `IdentityService.cs`,
    `ReportService.cs`, `ShiftWindowTests.cs` → `LocalDayTests.cs`.
  - AC: a midnight-crossing test passes.
  - V: `--filter LocalDay` · **CHECKPOINT**
  - Done as: pure `Domain/Services/LocalDay` (tz + `TimeProvider`); `ShiftClock` delegates to it and
    keeps only the shift windows until F3 retires handoff. Tests now compute "today" as a local day,
    and drift reads local-day windows (these failed every evening before).

### B — Domain core (pure, TDD)
- [x] **B1** Models `Session`, `Tick`, `SceneEvent` (+ kinds), with invariants.
  - Files: `Domain/Models/{Session,Tick,SceneEvent}.cs`, unit test.
  - AC: invalid ticks (negative counts, future > 5 min) are rejected.
- [x] **B2** `Rollup` with Merge and grains, using Tensors.
  - Files: `Domain/Models/Rollup.cs`, `Domain/Services/RollupMath.cs`, `RollupTests.cs` (CsCheck
    associativity + identity), props.
  - AC: property test passes over 10k samples.
- [x] **B3** Stats family A calculators: occupancy, dwell p50/p90/max, visits/h, peak concurrency,
  empty streak, busiest minute, stillness streak, entry/exit edges.
  - Files: `Domain/Services/PresenceStats.cs`, `SpaceStats.cs`, 2 test files.
  - AC: hand-computed fixtures match.
  - **CHECKPOINT**
  - Done as: dwell, visits, edges and a presence grid live in `Rollup` too (dwell as a quarter-octave
    histogram, so percentiles merge across any range). Sanitizer tests merged 9 → 2 to stay under the
    unit cap.
- [x] **B4** Family C calculators: hour×weekday, rhythm-score correlation, z-scores (|z| ≥ 2),
  7/30-day trend slope, busy-hour forecast. Replaces `DriftMath`.
  - Files: `PatternStats.cs`, `AnomalyMath.cs`, tests. (`DriftMath` stays until E4 removes the old
    drift panel that still uses it.)
- [x] **B5** Family D calculators: light curve, lights on/off steps, sunrise/sunset estimate, palette
  merge, word frequencies, weirdest caption (Jaccard).
  - Files: `EnvironmentStats.cs`, `CaptionStats.cs`, tests.
- [x] **B6** Achievements (≥ 15 definitions) and records; evaluation is idempotent.
  - Files: `Domain/Models/Achievement.cs`, `Domain/Services/AchievementRules.cs`, `RecordRules.cs`,
    tests.
  - AC: the same input twice unlocks once. **CHECKPOINT**
  - Done: 20 achievements, records that only move when beaten. Unit suite now sits at exactly
    100/100, so later tasks first merge adjacent tests in suites being retired.

### C — Storage, ingest, queries
- [x] **C1** Contracts plus in-memory repos: Session, Tick, SceneEvent, Rollup, AllTime, Achievement.
  - Files: `Application/Contracts/*` (2 files grouping the interfaces),
    `Infrastructure/Persistence/InMemoryStatsStores.cs`, DI.
- [x] **C2** Azure repos for Sessions, Ticks, Events; initializer tables.
  - AC: Azurite round trip; keys per SPEC §6.
  - V: `dotnet test tests/PoWatch.Integration --filter Repo`
- [x] **C3** Azure rollup + AllTime repo with ETag merge and retry.
  - AC: a concurrent-merge integration test passes. **CHECKPOINT**
  - Done: one `StatsStoreContractTests` script runs every store against in-memory and Azurite
    (3 tests), including 20 concurrent merges into one bucket. An `IIngestLedger` claims each
    batch key once so replays never double-count rollups.
- [x] **C4** `SessionService` + `/api/sessions` (start with tz, stop, list, get) + DTOs + JSON ctx.
  - AC: API E2E session lifecycle passes.
- [x] **C5** `IngestService` + `POST /api/sessions/{id}/batches` (FluentValidation; idempotency
  extended to `batchKey`; writes ticks and events, merges rollups; HybridCache tag eviction).
  - AC: replaying ×3 gives identical rollups; a future tick gets 400.
  - Done as: the durable `IIngestLedger` plus idempotent upserts handle replays, so the in-memory
    10-minute idempotency middleware was not extended. Accepted batches evict the user's stats cache tag.
- [x] **C6** `StatsQueryService` + `/api/stats/{family}?range=` (A–D, F) + the dev-only Bogus seed
  endpoint.
  - AC: Today ≤ 1.5 s and all-time over 365 seeded days ≤ 2 s (API E2E timing). **CHECKPOINT**
  - Done: six families (`presence`, `space`, `objects`, `patterns`, `environment`, `pipeline`) with
    HybridCache per user; seed endpoint exists only in Development/Test. API E2E merged 25 → 17
    before adding the session and stats tests (now 21/25).

### D — Client sensing
- [x] **D0 (spike, no commit unless adopted)** Background-tab behavior: worker timer +
  `requestVideoFrameCallback` + Wake Lock in Chrome/Edge. Findings are recorded in `tasks/plan.md`.
  - Done as a design note (headless Chromium cannot reproduce hidden-tab throttling): worker clock
    + Wake Lock + honest per-tick sample counts. Real-hardware check deferred to Phase 5.
- [x] **D1** L0 pixel layer: `wwwroot/js/sensing/pixel-layer.js` (luminance, 5-color palette, 16×9
  grid, reusing `computeFrameDiff`), a bridge export, and a `PixelSample` DTO.
  - AC: the System page shows L0 Hz ≥ 4.
  - Verified in headless Chromium with a fake camera: 19 samples in 5 s at a 250 ms interval
    (4 Hz steady), 144-cell grid, 5-colour palette, motion 0.13 on the moving test pattern.
    The System page readout comes with E5.
- [x] **D2** L1 detector: `wwwroot/js/sensing/detector-worker.js` (RF-DETR Nano, fallback
  RT-DETRv2) + `Shared/Services/Tracking/CentroidTracker.cs` (IoU match, 3 s gap merge, enter/exit edge)
  + tracker tests.
  - Verified in headless Chromium on Hugging Face's `cats.jpg`: RF-DETR Nano finds both cats (0.99,
    0.92) and both remotes (0.99, 0.89). WebGPU on Chromium's software GPU returned out-of-frame
    garbage, so the worker now runs a grey calibration frame after loading and falls back to WASM
    when boxes land outside the frame. WASM latency is about 1.5 s per frame on this CPU; the bridge
    skips frames while busy. The ≥ 0.8 Hz WebGPU target needs a real GPU (Phase 5).
  - Unit room: shift-window 4 → 2 and old significance 6 → 2 tests merged, all assertions kept.
- [x] **D3** `Shared/Services/Sensing/TickBatcher.cs` (10 s fold, 1 h replay queue, batch keys) +
  FakeTimeProvider tests + an ApiClient method.
- [x] **D4** L2 VLM: a structured JSON prompt, `Shared/Services/Sensing/CaptionParser.cs` (tolerant
  JSON), and **wire in `AdaptiveCadence`** (replacing the fixed delay).
  - Done as: the prompt asks for one plain sentence (small VLMs are unreliable at strict JSON);
    `CaptionParser` takes JSON when offered, otherwise keyword taxonomy. `VlmScheduler` maps
    smoothed pixel motion through `AdaptiveCadence`; the live sensing loop (E2) uses it.
- [x] **D5** `SyntheticSampleSource` (seeded, trim-safe) behind `MockInferenceService` / Demo mode.
  - AC: a fresh clone with no camera shows nonzero counters within 15 s. **CHECKPOINT**
  - Done: `SyntheticScene` (seeded walker, cat and mug) plus the client `SensingSession`, which
    drives pixel, detector and VLM (or the demo scene) into the tracker, batcher and outbox, and
    `LiveSensingState` for the UI. JS callbacks carry JSON text parsed by the source-generated
    context. The 15 s acceptance check runs through the Live page in E2.

### Phase 3 gate — done: "Terminal" chosen, component hierarchy confirmed (see SPEC §2a).

### E — UI (Radzen first, mission-control theme)
- [x] **E1** Theme tokens (dark default, `--rz-*` mapping, monospace numerals) + new nav (Live, Stats,
  History, Regulars, Trophies, System).
  - Done: `terminal.css` (self-hosted JetBrains Mono and Space Grotesk, `--rz-*` mapped, old tokens
    re-pointed), header, key bar with `PWCH>` command line, ticker, and panel primitives
    (`TerminalPanel`, `StatCell`, `Sparkline`, `ZChip`, `HeatGrid`). Number keys and Ctrl+1–6
    navigate (F-keys and Ctrl+R stay with the browser). The shell is one viewport tall and only
    `<main>` scrolls. Old routes keep aliases until their pages are rebuilt. Dev HTTP port is now 80.
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
