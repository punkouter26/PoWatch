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
                                                  ──► SignalR push (statsChanged)
                                                                          │
           Stats · History · Regulars  ◄── GET /api/stats/{family}, /api/recaps, …
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
| E · Pipeline | frames per layer, FPS, latency, detector confidence, uptime, storage | /system |

## Pages

`/` Live (one Start button; the demo scene is offered until a first session exists) · `/stats` four
tabs, a range picker and an "Ask" box, all on one bar · `/history/{yyyy-MM-dd}` the year and the day's
numbers beside three tabs (recap with motion, sessions and captions + PDF; moments; time-lapse) ·
`/regulars` a sortable, filterable grid: rename in place, tick several to merge · `/settings` every
optional choice (theme, sound, captions, watch rules) plus sign out, your data (CSV export, delete
everything) and the way to `/system` (connections, runtime, inference, pipeline). Range, tab, session and day live in the URL, so reloads, links and Back/Forward keep
their place. Every page needs a sign-in.

**Header.** Brand, four section keys, a status chip (state and uptime), Start/Stop, a bell and a gear
that opens Settings. The bell is the one notification tray: sensing problems, watch-rule alerts and
"name this newcomer" prompts; it opens by itself for a problem or an alert, and a prompt only raises
its count. Watch rules ("person between 22:00 and
06:00") are edited on Settings, kept in the browser, checked as things enter the frame, and relayed to
every open tab over the hub (plus a desktop notification where allowed).

**Keyboard.** `g` then `l s h r` jumps to a section, Space starts or stops, ← → step History's
day, Ctrl+K opens a command palette (also: the demo scene, System, full screen) (`wwwroot/js/menus.js`).

**Sound.** `wwwroot/js/cues.js` synthesises short cues with the Web Audio API (start, stop, something
entering — panned to the side it entered from — a new regular, an alert, an error). Level
off/low/mid/high is on Settings and remembered per browser.

**Motion grid.** On Live the 16×9 grid is a WebGL2 shader (`wwwroot/js/heat-gl.js`); without WebGL2
it is the plain grid. Animations use spring easings and all stop under `prefers-reduced-motion`.

**Offline.** The published app registers a service worker that caches the app shell (not the ~20 MB
vision runtime), so PoWatch opens without a network; batches wait in the outbox as before. Offline,
the shell stays open for whoever was last signed in on that browser; the server still decides what
the cookie may read.

**Comet trails** (`wwwroot/js/fx.js`, fed by `Layout/FxBridge.razor`) follow people and animals over
the camera and add up to a long-exposure PNG on the session recap; they respect
`prefers-reduced-motion`.

## API surface

| Route | Purpose |
|---|---|
| `POST /api/sessions`, `POST /api/sessions/{id}/stop`, `GET /api/sessions[?from=&to=]` | session lifecycle; `from`/`to` list every session overlapping a range |
| `POST /api/sessions/{id}/batches` | tick + event ingest (idempotent by `batchKey`) |
| `GET /api/stats/{presence\|space\|objects\|patterns\|environment\|pipeline}?range=` | stat families |
| `GET /api/recaps/session/{id}[.pdf]`, `GET /api/recaps/day/{date}[.pdf]?tz=[&ai=false]` | recaps and PDFs; `ai=false` answers from the template at once |
| `POST /api/ask` | a plain-language question answered from the caller's own statistics (503 without an AI provider) |
| `POST /api/alerts` | relay a fired watch rule to the user's open tabs |
| `GET /api/export.csv`, `DELETE /api/data` | one row per observed day; delete everything stored for the caller |
| `/api/regulars` (+ `/observe`, `/merge`, `PATCH /{id}`), `POST /api/snapshots`, `GET /api/snapshots/read`, `GET /api/sessions/{id}/moments` | regulars and highlights |
| `/hubs/stats` | SignalR: `statsChanged`, `alert` |
| `/health`, `/health/live`, `/diag/boot` (public), `/diag`, `/api/diagnostics/status` (signed in) | operations |

Unknown `/api/*` routes return 404. Everything except the SPA shell, `/health`, `/diag/boot` and
sign-in requires the BFF cookie (Entra ID, or guest sign-in in Dev/Test). `/api`, `/auth` and `/hubs`
are rate limited to 300 requests a minute per signed-in user (per IP when anonymous).

**Who may sign in.** `AzureAd:AllowedTenants` lists the tenant ids that may; it is compared with the
token's tenant exactly. Left empty, any Microsoft account can sign in and gets its own (empty) data
partition — production logs a warning at startup when that is the case.

## Storage

Azure Table Storage (Azurite locally), partitioned by user: `PoWatchSessions`, `PoWatchTicks`,
`PoWatchSceneEvents`, `PoWatchIngestLedger`, `PoWatchRollups`, `PoWatchRegulars`.
Blob containers: `snapshots`, `dataprotection-keys`. Retention: keep
everything. Storage is required: Azurite locally (docker compose) and in tests (Testcontainers).

## Recaps

`TemplateRecap` writes the paragraph from the numbers (Humanizer). If `AiProvider:Provider` is
`AzureOpenAi`, an `IChatClient` may rewrite the
paragraph only — never the numbers — and the template wins on timeout, error, or a reply that uses a
number not in the facts (`RecapPrompt.KeepsToFacts`). Azure OpenAI signs in with the app's Entra
identity when `AzureOpenAi:ApiKey` is empty (Development uses `gpt-5.4-nano` via the az CLI login;
Production stays on Template until the web app's identity has *Cognitive Services OpenAI User* on
`po-aiservices-shared`). Replies are cached by prompt, so reopening a day or its PDF costs nothing.
The model answers in a JSON schema (summary + highlights), so there is nothing to parse. Captions,
moment text and names reach the prompt as fenced data with control characters and the fence removed,
and the ingest validator caps their length. History paints the template recap first and swaps in
the model's version when it arrives; where the server has no model, a browser with a built-in one
(Chrome's Prompt API) rewords it locally under the same number check. "Ask PoWatch" uses the same
client with one tool, `get_stats(range)`, so the numbers in an answer come from the rollups.
QuestPDF renders the PDF; a host without its native engine answers an explained 503.

**Captions.** One on-device model, SmolVLM2 500M (WebGPU fp16 → fp32 → WASM q8). The prompt carries what the detector sees (`Visible: person x2, cat.`), an unchanged
scene is re-captioned at most every 2 minutes, SmolVLM runs without image splitting, and frames reach
the worker as transferred `ImageBitmap`s. A caption unlike the last 10 becomes a Notable moment. COOP
`same-origin` + COEP `credentialless` make the page cross-origin isolated, so the WASM backend runs
multi-threaded.

## Quality gates

- `TreatWarningsAsErrors`, `dotnet format --verify-no-changes`.
- Test caps (`SCRIPTS/check-test-caps.ps1`): 100 unit · 50 integration · 25 API E2E · 25 UI E2E.
- `SCRIPTS/check-hygiene.ps1`: layout, naming, central package versions, shell assets, and no
  caregiver-era vocabulary.
- CI runs unit, integration and API E2E; UI E2E runs locally (`E2E_LOCAL=1`).

## Deploy

Pushing to `master` runs `.github/workflows/deploy.yml` and deploys to production. Resource names are
shared between Bicep and CI in `infra/deployment.json`.
