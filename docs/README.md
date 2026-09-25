# PoWatch — project overview

PoWatch is a stat-heavy webcam app. Point a camera at anything, press **Start**, walk away, and come
back to a nerdy statistical picture of what happened. It is a single-user, hosted app: a Blazor
WebAssembly client served by an ASP.NET Core API on .NET 10.

This page is the source of truth for behavior; the code and tests fill in the detail.

## How it works

```
camera ──► browser (all sensing on-device)                        server
           L0 pixel layer   4 Hz  motion, light, palette, 16×9 grid
           L1 detector      ~1 Hz RF-DETR Nano → boxes → C# tracker  ──►  POST /api/sessions/{id}/batches
           L2 VLM caption   adaptive cadence, plain-sentence prompt       every 10 s: ticks + scene events
           TickBatcher      10 s ticks, outbox (IndexedDB) + replay       │
                                                                          ▼
                                                  IngestService: raw rows + rollups (minute/hour/day/all)
                                                  AchievementService: unlocks + records ──► SignalR push
                                                                          │
           Stats · History · Regulars · Trophies · wall  ◄── GET /api/stats/{family}, /api/recaps, …
```

- **Frames never leave the device.** The only images uploaded are highlight snapshots (first sighting
  of a class, motion spikes, notable captions), capped per hour and per day. Time-lapses stay in the
  browser.
- **Ticks, not frames, are the unit of ingest.** A batch has a `batchKey`; the server claims each key
  once, so a replayed batch is stored but never counted twice.
- **Rollups are mergeable.** Every stat query reads pre-aggregated minute/hour/day/all-time rollups
  (count/sum/sum²/min/max, 16×9 grids, class totals, palette and dwell histograms), except the few
  "raw" stats that read at most a day of events.

## Stat families

| Family | What | Where |
|---|---|---|
| A · Presence & space | occupancy, visits, dwell percentiles, peak at once, empty/still streaks, busiest minute, heatmaps, entry/exit edges | Stats → Presence & space |
| B · Objects & regulars | classes seen, rarest, regulars leaderboard (recognised by look, never by face) | Stats → Objects, /regulars |
| C · Patterns & anomalies | hour × weekday, rhythm score, "today vs usual" z-scores, trend, busy-hour forecast | Stats → Patterns |
| D · Environment & captions | light curve, lights on/off, daylight estimate, palette, word cloud, weirdest caption, recaps | Stats → Environment, History |
| E · Achievements & records | 20 achievements, 4 personal records, unlock toasts | /trophies |
| F · Pipeline | frames per layer, FPS, latency, detector confidence, uptime, storage | /system |

## Pages

`/` Live · `/stats` four tabs with a range picker · `/history` calendar → day (stats, recap + PDF,
sessions, moments, captions, time-lapse) · `/regulars` name/rename/merge · `/trophies` · `/display`
full-screen stats wall (settings menu → Stats wall) · `/system` (also `/health`) connections, runtime, inference, pipeline and model self-tests.

**Scene effects** (`wwwroot/js/fx.js`, fed by `Layout/FxBridge.razor`) come only from live numbers:
a Web Audio drone (light → pitch, motion → filter, who's in frame → chord), arrival plucks, anomaly
blips and teletype clicks; an announcer (speechSynthesis); a WebGL2 thermal shader in the Live motion
grid; comet trails over the camera plus a long-exposure PNG on the session recap; panel morphs
between sections (View Transitions); and a 3D occupancy terrain on the wall. Sound and announcer are
off until toggled in the header; motion effects respect `prefers-reduced-motion`; the plain HTML
grids stay underneath as the no-WebGL fallback.

## API surface

| Route | Purpose |
|---|---|
| `POST /api/sessions`, `POST /api/sessions/{id}/stop`, `GET /api/sessions` | session lifecycle |
| `POST /api/sessions/{id}/batches` | tick + event ingest (idempotent by `batchKey`) |
| `GET /api/stats/{presence\|space\|objects\|patterns\|environment\|pipeline}?range=` | stat families |
| `GET /api/recaps/session/{id}[.pdf]`, `GET /api/recaps/day/{date}[.pdf]?tz=` | recaps and PDFs |
| `GET /api/achievements` | trophy cabinet |
| `/api/regulars` (+ `/observe`, `/merge`, `PATCH /{id}`), `POST /api/snapshots`, `GET /api/snapshots/read`, `GET /api/sessions/{id}/moments` | regulars and highlights |
| `/hubs/stats` | SignalR: `statsChanged`, `achievementsUnlocked` |
| `/health`, `/diag`, `/api/diagnostics/status` | operations |

Unknown `/api/*` routes return 404. Everything except the SPA shell, `/health`, `/diag` and sign-in
requires the BFF cookie (Entra ID, or guest sign-in in Dev/Test).

## Storage

Azure Table Storage (Azurite locally), partitioned by user: `PoWatchSessions`, `PoWatchTicks`,
`PoWatchSceneEvents`, `PoWatchIngestLedger`, `PoWatchRollups`, `PoWatchRegulars`,
`PoWatchAchievements`. Blob containers: `snapshots`, `dataprotection-keys`. Retention: keep
everything. With no storage configured the API falls back to in-memory stores.

## Recaps

`TemplateRecap` writes the paragraph from the numbers (Humanizer). If `AiProvider:Provider` is
`Ollama` or `AzureOpenAi`, an `IChatClient` may rewrite the paragraph only — never the numbers — and
the template wins on timeout or error. QuestPDF renders the PDF; a host without its native engine
answers an explained 503.

## Quality gates

- `TreatWarningsAsErrors`, `dotnet format --verify-no-changes`.
- Test caps (`SCRIPTS/check-test-caps.ps1`): 100 unit · 50 integration · 25 API E2E · 25 UI E2E.
- `SCRIPTS/check-hygiene.ps1`: layout, naming, central package versions, shell assets, and no
  caregiver-era vocabulary.
- `tests/PoWatch.Benchmarks` (BenchmarkDotNet, not a test suite): rollup merge, summaries, a 30-day
  presence query. Run `dotnet run -c Release --project tests/PoWatch.Benchmarks` and pick a benchmark.

## Deploy

Pushing to `master` runs `.github/workflows/deploy.yml` and deploys to production. Resource names are
shared between Bicep and CI in `infra/deployment.json`.
