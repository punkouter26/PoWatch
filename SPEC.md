# PoWatch — Specification

Status: **Draft for approval** · Last updated 2026-09-24 · Module ownership lives in `CAPABILITY-MAP.md`.

## 1. Objective

PoWatch is a **fun, stat-heavy webcam observer**. You point a camera at anything (a room, a window, a
desk, a street, a bird feeder), press Start, and walk away. While it runs, PoWatch builds a deep, nerdy
statistical picture of what happened: presence, motion, space, objects, recurring "regulars", time
patterns, anomalies, environment, and captions. It also runs achievements and exposes its own pipeline
telemetry. When you come back, it tells you what you missed.

- **User:** one person, signed in (Microsoft Entra ID or dev/test guest), data hosted per user.
- **Not** a caregiving, safety, security, or medical product. There are no alerts or acknowledgment
  flows, and no clinical language.
- **Look:** dark, dense "mission control" dashboard with monospace numerals and sparklines everywhere,
  built with Radzen controls first.

## 2. User journeys

1. **Start a session.** Open `/` → pick a VLM (optional; the detector and pixel layers always run) →
   grant the camera → **Start**. A live overlay shows detection boxes and motion heat, with live
   counters and sparklines beside it.
2. **Walk away.** The tab keeps sensing. Every 10 s the client posts a compact *tick* batch, and the
   server updates the rollups. Adaptive cadence slows the VLM when the scene is static.
3. **Come back.** The **"While you were away"** card appears (triggered by Stop or by returning to a tab
   that was hidden for ≥ 5 min). It shows the elapsed time, the headline stats, the top 3 moments with
   snapshots, any new regulars, and any achievements unlocked.
4. **Dig into stats.** `/stats` has a range picker (Session · Today · 7d · 30d · All-time) and tabs:
   **Presence & Motion**, **Space**, **Objects & Regulars**, **Patterns & Anomalies**, **Environment &
   Captions**, **Pipeline**.
5. **Browse history.** `/history` shows a calendar heatmap of days. Pick a day to see its sessions,
   timeline, snapshots, and recap, with PDF export, plus that day's **time-lapse** when this device
   recorded one (see §6, on-device storage).
6. **Curate regulars.** When a new person (or cat or dog) has been in frame for a few seconds, the
   Live page shows a small, non-blocking **"New person spotted — name them?"** card with their
   snapshot. Typing a name saves it; ignoring it (or "Not now") lets the card fade after a minute and
   they stay "Person N". `/regulars` lists every recurring entity (people, pets, cars, objects) with
   visits, total time in frame and first/last seen, and lets you rename them and merge duplicates at
   any time.
7. **Trophies.** `/trophies` shows achievements (locked and unlocked) and all-time records.
8. **Stats wall.** `/display` is a full-screen, no-scroll, auto-refreshing dashboard for a second
   monitor or TV.
9. **System.** `/system` shows model readiness, a WebGPU self-test, storage, and pipeline telemetry.

## 2a. UI layout (Phase 3 decision: "Terminal")

Chosen from the 10 concepts on the design canvas (https://claude.ai/artifact/LChbbaQuhHyLXRg26N5URg,
board "1 · Terminal"). A dense Bloomberg-style grid of numbered panels, F-key navigation and a ticker.

```
MainLayout → TerminalShell
├─ TerminalHeader      one row: brand · FunctionKeyBar · status chip · Start/Stop · settings menu
│  ├─ FunctionKeyBar   1 LIVE · 2 STATS · 3 HISTORY · 4 REGULARS · 5 TROPHIES · 6 SYSTEM (number-key shortcuts);
│  │                   the active key names the section, so there is no separate page title
│  ├─ status chip      ● OBSERVING · CAMERA|DEMO · uptime · session # (· SAMPLE with a mock model)
│  ├─ settings menu    scene sound · announcer · theme · sign out
│  └─ CommandLine      PWCH> quick commands ("stats 7d", "regular bob", "start")
├─ AwayCard            session recap after any Stop, or "while you were away"
├─ @Body
└─ TickerTape          latest events
1 Live "/" → TerminalGrid (single column on phones):
  01 Camera · 02 Key metrics (incl. light + colours) · 03 In frame (tracks + census) · 04 Motion grid + edges
2 Stats: range picker + tabs Presence & space · Objects · Patterns · Environment
3 History: clickable year calendar → the day (stats + recap), sessions, moments, captions, time-lapse
4 Regulars: one table (rename in place, today, merge per row) · 5 Trophies: records + cabinet
6 System (/system, /health): connections, server, inference, pipeline counters, model self-test
/display: TerminalGrid full screen, no chrome, auto-refresh
Shared: TerminalPanel, StatCell, Sparkline, ZChip, HeatGrid, SampleDataChip
```

Radzen supplies the tables, charts, tabs and pickers. Per-row sparklines and heat cells are small
custom SVG components, because a chart instance per table row is too heavy.

## 3. Sensing pipeline (client, in-browser)

Frames never leave the browser except as chosen **highlight snapshots** (§6). The on-device
time-lapse (§6) stays in the browser too.

| Layer | Cadence | Output per sample |
|---|---|---|
| **L0 Pixel** | every animation frame, capped at 5 Hz | motion fraction (frame diff), mean luminance, 5-color palette, 16×9 motion grid |
| **L1 Detector** | ~1 Hz (adaptive 0.2–2 Hz) | `[{class, confidence, bbox}]`, which a centroid/IoU tracker turns into `trackId`, enter/exit edge, dwell |
| **L2 VLM** | adaptive 5–30 s (`VlmScheduler` → `AdaptiveCadence`, driven by smoothed pixel motion) | one plain caption sentence → `CaptionParser`: activities and weather from a keyword taxonomy (JSON is accepted when a larger model volunteers it; small models are unreliable at strict JSON) |

The client folds the samples into **10-second ticks**. A tick holds: motion mean/max, luminance mean,
palette, the motion-grid delta, per-class counts (max and mean), active track IDs, and FPS/latency per
layer. Discrete **scene events** are posted with the batch: track enter/exit, lights on/off (a
luminance step), VLM caption, notable moment, and achievement candidate.

## 4. Stat catalog (MVP = families A–F)

Every stat is computable for any range from the rollups (§6), unless it is marked *raw*.

**A. Presence, motion & space.** Occupancy % (the share of ticks with ≥ 1 person or animal track) ·
visits/hour · dwell-time distribution (p50/p90/max) · peak concurrency · longest empty streak · first
and last activity of the day · motion energy timeline · busiest minute · stillness streaks · spatial
activity heatmap (16×9) · entry/exit edge breakdown · path density (accumulated track centroids).

**Regular matching (no biometrics).** A regular is recognised by an appearance signature: a
64-bin colour histogram of the centre of its detector box, averaged over the track, compared by
cosine similarity (≥ 0.85, same class). This is not face recognition and stores nothing biometric,
so two people dressed alike can be confused and the same person in different clothes can appear as
new; merging on `/regulars` fixes both.

**B. Objects & regulars.** Object census (every class ever seen, with counts) · rarest sighting · new
or disappeared objects (a static object present for more than 30 min, then gone) · per-regular
leaderboard (visits, total dwell, first/last seen, arrival-time mean ± σ) · co-occurrence matrix (P(A
present | B present)).

**C. Patterns & anomalies.** Hour × weekday heatmap · GitHub-style calendar heatmap · daily rhythm score
(the correlation between today's hourly curve and the trailing 28-day mean) · today-vs-usual z-scores
per metric (flagged at |z| ≥ 2) · 7/30-day trends · next-busy-hour forecast (the hour with the highest
historical mean for the same weekday).

**D. Environment & captions.** Light-level curve · lights on/off events · estimated sunrise/sunset
(only when the luminance curve is daylight-shaped) · daily color palette strip · VLM-reported weather ·
activity taxonomy breakdown · caption word cloud · "weirdest caption" (lowest similarity to the
trailing captions) · auto-written session/day recap.

**E. Achievements & records.** At least 15 achievements (e.g. *First Light*: first session; *Night Owl*:
activity at 3 a.m.; *Menagerie*: 5 animal classes; *10k Frames*; *Marathon*: a 24 h session) · records
(longest session, busiest day, most concurrent, longest streak of daily sessions) · stat of the day ·
playful equivalents (the distance tracks moved through the frame, in pixels converted to "frame-widths").

**F. Pipeline telemetry.** Frames analyzed per layer · FPS · inference latency p50/p95 · mean detector
confidence · VLM tokens per second · uptime · storage used (rows and bytes) · ingest success rate.

## 5. Pinned tech stack

| Item | Version | Source |
|---|---|---|
| .NET SDK | 10.0.100 (`rollForward: latestMinor`) | `global.json` |
| ASP.NET Core / Blazor WASM | 10.0.6–10.0.9 | `Directory.Packages.props` |
| Radzen.Blazor | 10.2.3 | `Directory.Packages.props` |
| transformers.js (vendored) | 3.8.1 | `wwwroot/lib/transformers-3.8.1/` |
| VLMs | SmolVLM2-500M-Video-Instruct, SmolVLM-500M/256M-Instruct, Qwen2-VL-2B (onnx-community), Moondream 2 | `wwwroot/model-registry.json` |
| Object detector | `onnx-community/rfdetr_nano-ONNX` (Apache-2.0); fallback `onnx-community/rtdetr_v2_r18vd-ONNX` (Apache-2.0). Architectures confirmed in the vendored 3.8.1 bundle | HF Hub, verified 2026-09-24 |
| Microsoft.Extensions.AI (+ .OpenAI) / Azure.AI.OpenAI / OllamaSharp | 10.10.0 / 2.1.0 / 5.4.30 | recap providers |
| SignalR (server built-in; client on the ASP.NET 10.0.x line) | 10.0.x | live stats push |
| Humanizer.Core (server-only) / System.Numerics.Tensors | 3.0.10 / 10.0.12 | labels / SIMD math |
| Bogus (server + tests only) / BenchmarkDotNet | 35.6.5 / 0.15.8 | synthetic data / perf |
| CsCheck / Verify.Xunit / M.E.TimeProvider.Testing | 4.9.1 / 31.12.5 / 10.10.0 | tests |
| Azure.Data.Tables / Azure.Storage.Blobs | 12.11.0 / 12.23.0 | props |
| QuestPDF | 2025.4.0 | props |
| FluentValidation | 12.1.1 | props |
| Serilog / OpenTelemetry | 10.0.0 / 1.15.x | props |
| xUnit / Playwright / Testcontainers.Azurite | 2.9.3 / 1.61.0 / 4.11.0 | props |
| Local storage emulator | Azurite via `docker-compose.yml` | repo |

## 6. Data model & storage

Azure Table Storage (Azurite locally) plus Blob Storage. **Retention: keep everything, forever.** No
automatic deletion; storage size is surfaced as a stat (F).

| Table | PartitionKey | RowKey | Notes |
|---|---|---|---|
| `PoWatchSessions` | `{userId}` | session id | start/end, time zone; listed newest-first in memory (one user's sessions are few) |
| `PoWatchTicks` | `{userId}\|{yyyyMMdd}` (local day) | `{utcTicks:D19}-{sessionId}` | 10 s tick, ≤ 8,640 per day per session; grids stored as float bytes |
| `PoWatchSceneEvents` | `{userId}\|{yyyyMMdd}` (local day) | `{utcTicks:D19}-{stableId}` | enter/exit, captions, lights, notable; stable id makes replays idempotent |
| `PoWatchIngestLedger` | `{userId}` | batch key | claimed once, so a replayed batch never merges into rollups twice |
| `PoWatchRollups` | `{userId}\|{grain}` (`Minute`, `Hour`, `Day`, `AllTime`) | `{bucketStartUtcTicks:D19}` | mergeable aggregates; ETag read-merge-write with retry; all-time is the `AllTime` grain at the Unix epoch |
| `Regulars` / `RegularRevisions` | `{userId}` | regularId / revision | from Subjects |
| `PoWatchAchievements` | `{userId}` | `a:{id}` unlocks · `r:{id}` records | first unlock time wins; records only move when beaten |
| Blob `snapshots/` | `{userId}/{yyyyMMdd}/{eventId}.jpg` | — | highlight frames only |

The **ingest path** validates the batch, upserts ticks and events idempotently (by batch key), and
merges them into minute/hour/day rollups plus AllTime. Stat queries read rollups only, except the stats
marked *raw* (weirdest caption, word cloud), which read at most one day of events.

**Highlight snapshot rules.** A snapshot (640 px JPEG, uploaded straight from the browser with a 5-minute write-only link) is kept for: the first sighting of each class in a session, a motion spike (pixel motion ≥ 0.15, at most one per 10 minutes) and a caption the model flags as notable. The browser keeps at most 20 an hour; the server issues at most 200 upload links per user per local day, only under that user's own {user}/{yyyyMMdd}/ prefix, and read links only for the owner. Each snapshot is recorded as a Notable scene event with its path; demo mode and in-memory storage keep the moment without a picture.

**On-device storage (IndexedDB, never uploaded).** `powatch-outbox` holds every ingest batch until
the server acknowledges it (capped at 360 ≈ 1 h), so a reload, crash or closed tab loses nothing:
leftovers from earlier page loads are re-posted when the app opens and when a session starts.
`powatch-timelapse` holds one 480 px JPEG a minute while a camera session runs, keyed by local day and
pruned after 7 days. `/history` plays a day's frames as a flipbook and records a WebM with the native
`MediaRecorder`, so there is no extra dependency. The app is installable (web manifest).

## 7. Commands

```powershell
.\SCRIPTS\setup.ps1                                        # tooling + Azurite
dotnet restore PoWatch.slnx
dotnet build PoWatch.slnx -c Release                       # warnings are errors
dotnet format PoWatch.slnx --verify-no-changes             # lint / style gate
dotnet run --project src/PoWatch.Api/PoWatch.Api.csproj    # http://localhost (port 80)
dotnet test tests/PoWatch.Unit -c Release
dotnet test tests/PoWatch.Integration -c Release           # needs Docker (Azurite testcontainer)
dotnet test tests/PoWatch.E2EAPI -c Release
$env:E2E_LOCAL=1; dotnet test tests/PoWatch.E2EUI -c Release
./SCRIPTS/check-test-caps.ps1; ./SCRIPTS/check-hygiene.ps1
dotnet run -c Release --project tests/PoWatch.Benchmarks    # BenchmarkDotNet, not a test suite
```

## 8. Project structure

```
src/
  PoWatch.Domain/          Models/ (Session, Tick, SceneEvent, Rollup, Regular) Services/ (stat calculators, achievement + record rules)
  PoWatch.Application/     Contracts/ Services/ Options/
  PoWatch.Infrastructure/  Persistence/ (Azure* + InMemory*) Runtime/ (recap AI, diagnostics)
  PoWatch.Api/             Features/{Sessions,Ingest,Stats,Regulars,Snapshots,Recaps,Achievements,Diagnostics,Auth,Dev}/
  PoWatch.Shared/          Models/ (DTOs) Services/ (shared pure helpers)
  PoWatch.Client/          Pages/ (Live, Stats, History, Regulars, Trophies, Display, System, Health, Login)
                           Components/ Shared/ Services/ wwwroot/js/sensing/
tests/  PoWatch.Unit  PoWatch.Integration  PoWatch.E2EAPI  PoWatch.E2EUI  PoWatch.Benchmarks
```

## 9. Code style & conventions

- Compiler contract from `Directory.Build.props`: nullable on, `TreatWarningsAsErrors`, analyzers
  `Recommended`, and code style is enforced during the build. Every `.editorconfig` opt-out gets a
  justification on the same line.
- Vertical-slice minimal APIs: one static `Map{Feature}Feature` per `Features/{Feature}` folder, with
  groups that require authorization.
- The client and Shared stay trim-clean: JSON goes through a source-generated `PoWatchJsonContext`,
  with no reflection serialization.
- Domain math is pure and takes timestamps as input. The architecture-boundary unit tests enforce the
  module rules.
- Config lives in `appsettings*.json` or Azure Key Vault. **No `dotnet user-secrets`** (per
  `AGENTS.md`). Nothing secret is committed.

```csharp
internal static class StatsEndpoints
{
    internal static IEndpointRouteBuilder MapStatsFeature(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stats").WithTags("Stats").RequireAuthorization();

        group.MapGet("/presence", async (
            StatsRange range,
            StatsQueryService service,
            CancellationToken cancellationToken) =>
        {
            var stats = await service.GetPresenceAsync(range, cancellationToken);
            return Results.Ok(stats.ToDto());
        });

        return app;
    }
}
```

## 10. Testing strategy

| Level | Framework | Cap | Focus |
|---|---|---|---|
| Unit | xUnit 2.9.3 | 100 | stat calculators against hand-computed fixtures, rollup merge associativity, tracker, achievement rules, architecture boundaries |
| Integration | xUnit + Testcontainers.Azurite | 50 | ingest → rollup → query round trip, idempotent batch replay, repositories |
| API E2E | xUnit + `WebApplicationFactory` | 25 | auth, session lifecycle, stats endpoints, recap PDF |
| UI E2E | Playwright (Chromium) | 25 | navigation, live page with **mock inference**, stats tabs render, stats wall no-scroll, mobile/desktop viewports |

- Strict TDD per task: Red → Green → Build → Commit.
- Coverage target: **≥ 85% line coverage on `PoWatch.Domain` stat calculators**, ≥ 70% on
  `PoWatch.Application`. There is no gate on the UI or Infrastructure.
- The mock inference service emits deterministic synthetic L0/L1/L2 samples, so the app runs end to end
  without a camera, GPU, model weights, or AI keys.

## 11. Boundaries

- **Always:** work on `master` · keep warnings at zero · run the related tests · restart the app and
  verify it's healthy after changes · keep `SPEC.md` and `tasks/todo.md` in sync · use Radzen controls
  first.
- **Ask first:** new infrastructure or paid Azure resources · raising test caps · adding a JS/NuGet
  dependency outside this spec · changing auth · any deploy · destructive storage operations.
- **Never:** send raw frames off-device except highlight snapshots · use face recognition or biometric
  identity · commit secrets · use `dotnet user-secrets` · skip, weaken, or delete failing tests · add
  caregiver, clinical, or safety wording.

## 12. Out of scope (MVP)

Custom natural-language counters (the next trajectory) · data export/API · multi-camera · highlight
reels · public multi-user accounts, sharing, and leaderboards · push notifications ·
audio analysis · native mobile apps · any safety or alerting use case.

## 13. Edge cases

- Camera permission denied or no camera → an explanatory empty state and a **Demo mode** (mock
  inference).
- No WebGPU → WASM fallback dtype. If the VLM is too slow (> 30 s per caption), run detector + pixel
  only and show a notice.
- Tab hidden or throttled → ticks carry the actual sample counts, so rates stay honest. Gaps are shown
  as gaps, not zeros.
- Laptop sleep or network loss → the client queues up to 1 h of batches in IndexedDB (§6) and replays
  them with idempotency keys, also after a reload or crash. An expired sign-in, a timeout or rate
  limiting counts as "retry later", not as a refusal. Anything older is dropped and counted as "lost
  ticks" (F).
- A session crosses midnight → ticks are bucketed by the user's local day, and the session is linked
  from both days.
- Scene changes (camera moved) → a luminance/palette discontinuity starts a new "scene epoch", so the
  anomaly baselines don't compare apples to oranges.
- Tracker ID churn (occlusion) → merge within a 3 s gap. Regulars can still be merged manually.
- Clock skew → the server stamps receipt time and rejects ticks more than 5 min in the future.
- Very long sessions (7 d or more) → no unbounded client memory: rolling buffers are capped.

## 14. Error states

| Condition | UI | Server |
|---|---|---|
| Storage unavailable | Banner: "Stats are paused. Sensing continues locally." | 503 + readiness down |
| Ingest 4xx (validation) | System pipeline counter increments | Problem details, logged |
| Model download fails | System page error + retry, detector-only fallback | — |
| AI recap provider down | Template recap, labelled as such | fallback logged |
| Auth expired | Redirect to login, keep the local queue | 401 |

## 15. Success criteria (measurable)

1. With **mock inference**, a fresh clone runs `setup.ps1` → `dotnet run` → sign in as guest → Start →
   and sees nonzero live counters within 15 s, with no Azure credentials or AI keys.
2. On the reference desktop (WebGPU), L0 sustains ≥ 4 Hz and L1 ≥ 0.8 Hz while L2 captions at least
   every 60 s. This is measured on System (pipeline panels) over a 10 min session.
3. A 2 h session keeps the tab's JS heap under 1 GB (DevTools memory snapshot at 0 h and 2 h).
4. `/stats` Today with 8,640 ticks loads in ≤ 1.5 s. All-time with 365 synthetic days loads in ≤ 2.0 s
   (API E2E timing test).
5. The "While you were away" card renders within 3 s of Stop or of returning to the tab.
6. All six stats tabs (A–D, F, plus trophies for E) render with synthetic data, with no console errors
   (UI E2E).
7. Every stat in §4 has at least one unit or integration test with a hand-computed expected value, and
   rollup merges are proven associative (unit).
8. Replaying the same ingest batch 3 times produces identical rollups (integration).
9. At least 15 achievements are defined, and unlocks are idempotent (unit + integration).
10. `/display` at 1920×1080 and 1280×720 has no scrollbars and refreshes at least every 5 s (UI E2E).
11. Pages at 390 px width have no horizontal scroll (UI E2E viewport tests).
12. Session and day recap PDFs download and are valid PDF documents (API E2E).
13. The hygiene script finds **zero** hits in `src/` and `tests/` for: `caregiver`, `clinical`,
    `handoff`, `nurse`, `patient`, and `shift`/`acknowledge` in their caregiver sense (shift
    window/clock/report, mid-shift, acknowledgement). Plain "shift" and batch "acknowledge" are fine.
14. Network inspection during a 5 min session shows no image payloads except highlight snapshot
    uploads.
15. The Release build has 0 warnings, `dotnet format --verify-no-changes` passes, and the test caps
    hold.

## 16. Subsystem disposition

See `CAPABILITY-MAP.md` → *Subsystem disposition*. In summary:

- **Retained:** BFF auth, telemetry, Key Vault, Data Protection, adaptive cadence,
  health/diagnostics. Request-level idempotency was replaced by the batch-key ledger.
- **Refactored:** Observer Hub → Live · People → Regulars · Archives → History/Stats · Handoff → Recaps
  · Drift → Anomaly · Evidence → Snapshots · Kiosk → Stats wall · Significance → notable-moment score.
- **Pruned:** urgent alerts, threshold rules, acknowledgment, clinical tag parser, shift clock, alert
  audio, and the System page's "clear all data" reset (retention is keep-everything).
- **Status:** done — tasks F3–F5 and G1–G2 removed the last of the old code paths.

## 17. Decisions (Phase 2) and open questions

Decided:
- **Detector:** RF-DETR Nano (§5). YOLOv10 is excluded because of its AGPL-3.0 license.
- **Test caps:** they stay at **100 / 50 / 25 / 25**, with breadth covered by CsCheck property tests.
  When a suite is full, adjacent tests in suites being retired are merged (every assertion kept).
- **Ponytail** (MIT Claude Code plugin) is installed before Phase 4, and `/ponytail-review` joins
  Phase 5.
- **Existing production data:** the caregiver-era tables and the `significant-images` container are
  **deleted at the first deploy**. This is task G5 and needs explicit confirmation at that moment.
- **Pushing to `master` deploys to production.** Build work commits locally only, and pushing is a
  gated deploy.

Still open:
1. **Keep-forever cost:** revisit after 30 days of real data, using the "storage used" stat.
2. **Background-tab throttling:** spike D0 decides the sampling driver.
