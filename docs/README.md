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
           Live · History · Regulars   ◄── GET /api/stats/{family}, /api/recaps, …
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
| A · Presence & space | occupancy, visits, dwell percentiles, peak at once, empty/still streaks, busiest minute, heatmaps, entry/exit edges | Live |
| B · Objects & regulars | classes seen, rarest, regulars leaderboard (recognised by look, never by face) | Live, /regulars |
| C · Patterns & anomalies | hour × weekday, rhythm score, "today vs usual" z-scores, trend, busy-hour forecast | Live |
| D · Environment & captions | light curve, lights on/off, daylight estimate, palette, word cloud, weirdest caption, recaps | Live, History |
| E · Pipeline | frames per layer, FPS, latency, detector confidence, uptime, storage | /system |

## Pages

`/` Live, one screen under a range picker and an "Ask" box: a 4×4 grid of the camera (2×2, one
Start button; the demo scene is offered until a first session exists), key metrics, who is in frame,
the live motion grid and nine stat panels. The grid shows the headlines; "All data ↓" on the bar
downloads every number behind them as one JSON file · `/history/{yyyy-MM-dd}` the year (with the
weather's effect, once a place is set), the day's numbers and weather, and the week against the week
before, beside four tabs (recap with motion, sessions and captions + PDF + share link; replay; moments;
time-lapse) ·
`/regulars` a sortable, filterable grid: rename in place, tick several to merge · `/settings` every
optional choice (theme, sound, captions, watch rules), what the server keeps for you (retention
window, weather place, weekly email), your shared recap links, plus sign out, your data (CSV export,
delete everything) and the way to `/system` (connections, runtime, inference, pipeline). Range, session and day live in the URL, so reloads, links and Back/Forward keep
their place. Every page needs a sign-in.

**Header.** Brand, three section keys, a status chip (state and uptime), Start/Stop, a bell and a gear
that opens Settings. The bell is the one notification tray: sensing problems, watch-rule alerts,
"name this newcomer" prompts and, once a week, last week in review; it opens by itself for a problem
or an alert, and the rest only raise its count. Watch rules ("person between 22:00 and
06:00") are edited on Settings, kept in the browser, checked as things enter the frame, and relayed to
every open tab over the hub (plus a desktop notification where allowed).

**Keyboard.** `g` then `l h r` jumps to a section, Space starts or stops, ← → step History's
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

**Tube switch** (`wwwroot/js/crt.js`). Start and Stop play a short CRT power-on and power-off over the
camera panel (a transparent WebGL2 overlay, so camera and demo alike), timed with the start and stop
cues; Stop keeps the picture docked until the tube is dark. Nothing plays under reduced motion.

**The year in 3D** (History, `wwwroot/js/skyline.js`). The year panel switches between the 2D
calendar and a skyline of instanced WebGL2 towers (height = occupancy, blue = a wet day): drag or
arrow keys to turn, scroll or + − to zoom, hover for the day, click to open it. "Fly over" glides
along the year and plays each week as a note (minor pentatonic, higher when busier). It draws only on
change, never in an idle loop, since the detector and the caption model share the GPU.

**Replay** (History, `wwwroot/js/replay.js`). A day minute by minute from `GET /api/stats/replay`:
a track of the whole day (occupancy bars, motion line, moment ticks, caption dots) under a native
range slider (drag, ← →, Home/End); the playhead drives the motion grid shader, the readout, the
caption of the moment and, where this browser kept a time-lapse, the nearest camera frame. Play walks
the observed minutes only.

**Comet trails** (`wwwroot/js/fx.js`, fed by `Layout/FxBridge.razor`) follow people and animals over
the camera and add up to a long-exposure PNG on the session recap; they respect
`prefers-reduced-motion`.

## API surface

| Route | Purpose |
|---|---|
| `POST /api/sessions`, `POST /api/sessions/{id}/stop`, `GET /api/sessions[?from=&to=]` | session lifecycle; `from`/`to` list every session overlapping a range |
| `POST /api/sessions/{id}/batches` | tick + event ingest (idempotent by `batchKey`) |
| `GET /api/stats/{presence\|space\|objects\|patterns\|environment\|pipeline}?range=` | stat families |
| `GET /api/stats/all?range=` | every family in one JSON document, plus the raw ticks and events for session, today or one day (Live's "All data ↓", History's "Day data ↓") |
| `GET /api/recaps/session/{id}[.pdf]`, `GET /api/recaps/day/{date}[.pdf]?tz=[&ai=false]` | recaps and PDFs; `ai=false` answers from the template at once |
| `POST /api/ask` | a plain-language question answered from the caller's own statistics (503 without an AI provider) |
| `POST /api/alerts` | relay a fired watch rule to the user's open tabs |
| `GET /api/export.csv`, `DELETE /api/data` | one row per observed day; delete everything stored for the caller |
| `/api/regulars` (+ `/observe`, `/merge`, `PATCH /{id}`), `POST /api/snapshots`, `GET /api/snapshots/read`, `GET /api/sessions/{id}/moments` | regulars and highlights |
| `GET /api/stats/replay?date=&tz=` | one day's observed minutes with their 16×9 motion grids (base64 bytes), and its captions, moments, entrances and exits |
| `GET`/`PUT /api/profile`, `GET /api/places?q=` | retention window, weather place, weekly email; place search (Open-Meteo) |
| `GET /api/weather/year?tz=` | a year of daily weather at the caller's place and, with enough days, how wet days compare with dry |
| `GET /api/digest/week?date=&tz=` | the Monday-to-Sunday week holding a day against the week before |
| `POST`/`GET /api/shares`, `DELETE /api/shares/{token}` | make, list and take down shared recap links (1, 7 or 30 days) |
| `/s/{token}`, `/s/{token}/card.png` (public) | a shared recap as plain HTML (no scripts, `noindex`, strict CSP, `no-store`) and its 1200×630 preview card |
| `/hubs/stats` | SignalR: `statsChanged`, `alert` |
| `/health`, `/health/live`, `/diag/boot` (public), `/diag`, `/api/diagnostics/status` (signed in) | operations |

Unknown `/api/*` routes return 404. Everything except the SPA shell, `/health`, `/diag/boot`, shared
recap pages (`/s/…`) and sign-in requires the BFF cookie (Entra ID, or guest sign-in in Dev/Test).
`/api`, `/auth`, `/hubs` and `/s` are rate limited to 300 requests a minute per signed-in user (per IP
when anonymous).

**Who may sign in.** `AzureAd:AllowedTenants` lists the tenant ids that may; it is compared with the
token's tenant exactly. Left empty, any Microsoft account can sign in and gets its own (empty) data
partition — production logs a warning at startup when that is the case.

## Storage

Azure Table Storage (Azurite locally), partitioned by user: `PoWatchSessions`, `PoWatchTicks`,
`PoWatchSceneEvents`, `PoWatchIngestLedger`, `PoWatchRollups`, `PoWatchRegulars`, `PoWatchProfiles`;
`PoWatchShares` is keyed by token (and deleted by owner on "delete everything").
Blob containers: `snapshots`, `dataprotection-keys`. Storage is required: Azurite locally (docker
compose) and in tests (Testcontainers).

**Retention.** Everything is kept unless the user picks a window (30 days, 90 days or a year) in
Settings. A background job (every 6 h) then deletes that user's raw ticks, scene events, snapshots
and ingest claims older than the window. Rollups are never pruned, so charts, the calendar and
all-time numbers keep the whole history; replays, captions and moments of older days go.

**Weather.** Optional context from Open-Meteo (free, no key): the user picks a place by name; History
shows each day's weather and sun, and once there are three or more wet and dry observed days each,
whether wet days run busier. Older days come from the reanalysis archive (cached a day), the last
week from the forecast API (cached an hour); a failed read leaves the days out.

**Weekly digest.** The week in review is computed on demand from the day rollups (History's "The
week" panel; the bell offers last week once per browser). It is also mailed from Monday 08:00 in the
user's zone when they turn the weekly email on, which needs `Digest:Email:ConnectionString` and
`Digest:Email:Sender` (Azure Communication Services Email) and, for the link back, `Digest:AppUrl`.
Without them the switch is disabled and says why.

**Sharing.** "Share link" on a day's recap freezes it (without moments, whose snapshot links are
private) behind a 128-bit random token for 1, 7 or 30 days. Anyone with the link reads the frozen
copy; the owner can take it down from History or Settings, and "delete everything" removes it.

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
- CI runs all four suites; UI E2E installs Playwright's Chromium and runs with `E2E_LOCAL=1`.

## Deploy

Pushing to `master` runs `.github/workflows/deploy.yml` and deploys to production. Resource names are
shared between Bicep and CI in `infra/deployment.json`.
