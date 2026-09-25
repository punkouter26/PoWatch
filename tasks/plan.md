# PoWatch — Implementation Plan (Phase 2)

Source of truth for architecture, risks and checkpoints. Behavior lives in `SPEC.md`; tasks in `tasks/todo.md`.

## Decisions locked in during Phase 2
- **Libraries:** RF-DETR Nano detector · Microsoft.Extensions.AI · SignalR · Humanizer ·
  System.Numerics.Tensors · Bogus · BenchmarkDotNet · FakeTimeProvider · CsCheck · Verify.Xunit.
- **Test caps stay 100/50/25/25.**
- **Ponytail** gets installed before Phase 4.
- **Old caregiver storage gets deleted at deploy.** Destructive, so it's confirmed again at that moment.
- **Pushing to `master` triggers a production deploy** (`.github/workflows/deploy.yml:98`). Build tasks
  commit locally only. Any push is a deploy and needs your explicit go-ahead.
- **Where `AGENTS.md` wins over the pasted workflow:** run related tests per task, and the full suite
  only in Phase 5 · no `dotnet user-secrets` · restart the app and check `/health` after each task.

### Verified facts (research)
- **Detector support:** the vendored transformers.js **3.8.1** (`wwwroot/lib/transformers-3.8.1/`)
  already contains the `rf_detr`, `d_fine` and `rt_detr_v2` architectures (checked by grepping the
  vendored bundle).
- **Detector:** `onnx-community/rfdetr_nano-ONNX`. Apache-2.0, `object-detection` pipeline, and there's
  a WebGPU demo Space (`webml-community/RF-DETR-Nano-WebGPU`).
  - **Fallback:** `onnx-community/rtdetr_v2_r18vd-ONNX` (Apache-2.0).
  - **Excluded:** YOLOv10 (AGPL-3.0).
- **NuGet latest stable (2026-09-24):**
  - Microsoft.Extensions.AI and M.E.AI.OpenAI 10.10.0
  - OllamaSharp 5.4.30
  - Humanizer.Core 3.0.10
  - System.Numerics.Tensors 10.0.12
  - Bogus 35.6.5
  - BenchmarkDotNet 0.15.8
  - M.E.TimeProvider.Testing 10.10.0
  - CsCheck 4.9.1
  - Verify.Xunit 31.12.5
  - Azure.AI.OpenAI 2.1.0
  - SignalR.Client: pin to the repo's existing ASP.NET 10.0.x line.
- **Ponytail:** an MIT Claude Code plugin. Install with `claude plugin marketplace add DietrichGebert/ponytail`,
  then `claude plugin install ponytail@ponytail`. It adds Node lifecycle hooks and `/ponytail-review`.

### Current-code facts that shape the plan
- **Frame diff already exists:** `computeFrameDiff` in `wwwroot/js/inference-bridge.js:101` (120×68
  canvas, delta > 28). `captureFrame` is at `:139` and `captureAndInfer` at `:279`. L0 builds on these.
- **Worker protocol:** id-correlated `postToWorker` (`inference-bridge.js:53`). The VLM worker's
  `prepareInputs`/`runInference` are at `inference-worker.js:275/352`, with quality gates at 472–627.
- **`AdaptiveCadence` is not wired in.** The loop uses a fixed delay
  (`ObserverHub.Monitoring.razor.cs:172`). D4 wires it up.
- **The client never sends an `IdempotencyKey`.** `IdempotencyMiddleware` only covers
  `/api/observer/ingest`.
- **Storage choice:** Azure vs in-memory is picked by factory lambdas in
  `Infrastructure/DependencyInjection.cs:57-97`, and the initializer creates the tables. Every new repo
  follows that pattern.
- **Test fixtures to reuse:** `AzuriteWebApplicationFactory` (Integration), `ApiE2EFactory`, and
  `PlaywrightFixture` with `E2E_LOCAL=1`.
- **Architecture rules:** enforced by `tests/PoWatch.Unit/ArchitectureBoundaryTests.cs`.
- **Client patterns:** `PoWatchJsonContext` source-gen (one `[JsonSerializable]` per DTO), thin
  `PoWatchApiClient` wrappers, and `SafeJsInterop`.
- **Charts:** only `Shared/DriftDetailPanel.razor` uses `RadzenChart` today. `DailyActivityHeatmap` is
  custom and gets reused for the grid and calendar heatmaps.

### Architecture decisions
1. **JS handles pixels and models only.** Tracker, tick batcher, caption parser and highlight rules are
   pure C# in `PoWatch.Shared/Services/{Tracking,Sensing}`, so they are xUnit-testable and trim-clean
   when they run in WASM.
2. **Two workers.** The existing VLM worker, plus a new `detector-worker.js`. Both load transformers.js
   from our own origin, and both are orchestrated by `inference-bridge.js`. The detector gets priority.
   The VLM runs only when `AdaptiveCadence` allows and the detector is idle.
3. **The tick (10 s) is the unit of ingest.** The client posts `IngestBatchDto` (ticks + events +
   `batchKey` GUID) about every 10 s. The server keeps raw ticks and events forever.
4. **Rollups are mergeable aggregates.** `Rollup` holds count/sum/sum²/min/max per metric, class-count
   maps, a 16×9 grid and a palette histogram. Grains are minute, hour, day and AllTime. Writes use
   ETag optimistic concurrency with retry. Every stat query reads rollups, except the stats marked
   *raw* (at most one day of events).
5. **Partitioning:** every table is partitioned by the BFF user id claim. Local day comes from the IANA
   time zone sent at session start (the tz is stored on the Session).
6. **Idempotency:** the middleware is extended to `/api/sessions/{id}/batches` using `batchKey`, and
   rows use deterministic keys, so replaying a batch is a no-op at the storage layer too.
7. **Live push:** SignalR `/hubs/stats` (cookie auth, same origin). The server pushes `RollupDeltaDto`
   after ingest. The client protocol uses `PoWatchJsonContext` in the resolver chain, which keeps it
   trim-safe. `PollLoop` is the fallback.
8. **Recaps:** `RecapService` sits on M.E.AI `IChatClient`: Azure OpenAI via
   `AzureOpenAIClient…AsIChatClient()`, Ollama via `OllamaApiClient`. `TemplateRecap` is the default and
   the failure fallback. Humanizer is **server-only**.
9. **Bogus is server- and test-only** (it uses reflection, which isn't trim-safe). Client demo mode
   uses a small seeded `SyntheticSampleSource` in Shared. A dev-only `POST /api/dev/seed?days=365` uses
   Bogus to build synthetic history for demos and perf tests.
10. **Subject → Regular rename:** done as one mechanical rename commit (T-F1) with no behavior change.
    It's exempt from the 5-file rule and verified by build + existing tests.
11. **Old caregiver tables/containers:** stop being created or read (G1). Deleting them in production
    is a gated deploy step (G5).

### Dependency graph
```
A1..A4 (prune) ──► B1 ──► B2 ──► B3,B4,B5 ──► B6
                         │
                         └► C1 ──► C2,C3 ──► C4 ──► C5 ──► C6 ──► E6
D0 (spike) ──► D1 ──► D2 ──► D3(needs B1) ──► D4 ──► D5
Phase 3 design ──► E1 ──► E2(needs D3,C5) ──► E3..E5(needs C6) ──► E7 ──► E8 ──► E9
C4 ──► F1 ──► F2(needs D2) ; B6+C5 ──► F5 ; C6 ──► F3 ──► F4
all ──► G1 ──► G2 ──► G3 ──► G4 ──► (G5 deploy, gated)
```

### Risks
| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Hidden or background tab throttles rAF/timers, so "leave it running" stalls | High | High | D0 spike: drive sampling from a worker timer plus `requestVideoFrameCallback`, and hold a Screen Wake Lock. Show gaps honestly. Document which browsers behave |
| Detector + VLM on one GPU → memory pressure or long frames | Med | High | Serialize: detector first, VLM only when the detector is idle. Detector-only mode. Fallback dtypes (fp32 → wasm q8) |
| RF-DETR download size or first-load time | Med | Med | Load lazily after Start. Progress bar. transformers.js browser cache. Size recorded in the System tab |
| SignalR client trips trim/AOT analyzers (warnings are errors) | Med | Med | E6 starts with a build-only spike. Fall back to `PollLoop` if it can't be made warning-free |
| Unit cap 100 is tight | High | Med | Budget table below. CsCheck property tests cover breadth. Delete caregiver tests during pruning |
| ETag conflicts on rollup rows | Low | Low | Single user + retry with a fresh read. Covered by an integration test |
| Keep-forever storage cost | Med | Low | "Storage used" stat. Revisit after 30 days of real data |
| Deleting old production data is irreversible | — | High | G5 is gated: explicit confirmation at deploy time, plus a listing of what will be deleted |
| Pushing to master deploys to production | — | High | Local commits only. Push only on explicit "deploy/sync" |

### Test budget (caps unchanged)
| Suite | Cap | Plan |
|---|---|---|
| Unit | 100 | ~25 retained (architecture, cadence, idempotency, identity/merge, sanitizer, masking, DI) + ~70 new: tracker 5, batcher 4, caption parser 3, rollup 3 (1 CsCheck), stats A 8, C 7, D 6, achievements 5, highlight rules 3, recap 3 (Verify), anomaly 3, misc 20 |
| Integration | 50 | ~20 retained + ~25 new: repos 8, ingest/replay 5, rollup concurrency 2, stats queries 6, SignalR 2, recap 2 |
| API E2E | 25 | rewritten journeys: auth 4, session lifecycle 5, stats 8 (including perf timing), recap PDF 3, regulars 3, achievements 2 |
| UI E2E | 25 | shell/nav 4, live with mock 4, stats tabs 6, stats wall 3, history 3, regulars 2, viewports 3 |

### Checkpoints
After A4, B3, B6, C3, C6, D5, E5, E9, F5 and G4:
1. Build Release with zero warnings.
2. `dotnet format --verify-no-changes`.
3. Run the suites the last group touched.
4. `check-test-caps.ps1` and `check-hygiene.ps1`.
5. Restart the app and confirm `/health` returns 200.
6. Update `SPEC.md` and `todo.md` if anything drifted.

### Top 10 implementation examples (chosen items)
1. **RF-DETR in `detector-worker.js`**
   ```js
   const detector = await pipeline('object-detection', 'onnx-community/rfdetr_nano-ONNX', { device: 'webgpu', dtype: 'fp32' });
   const out = await detector(frameUrl, { threshold: 0.5 });   // [{ label, score, box:{xmin,ymin,xmax,ymax} }]
   ```
2. **Rollup merge with Tensors (Domain)**
   ```csharp
   public static Rollup Merge(Rollup a, Rollup b) { var grid = new float[144]; TensorPrimitives.Add(a.Grid, b.Grid, grid); return a with { Count = a.Count + b.Count, Sum = a.Sum + b.Sum, SumSq = a.SumSq + b.SumSq, Min = Math.Min(a.Min, b.Min), Max = Math.Max(a.Max, b.Max), Grid = grid, Classes = MergeCounts(a.Classes, b.Classes) }; }
   ```
3. **CsCheck associativity (one unit test, thousands of cases)**
   ```csharp
   Gen.Select(GenRollup, GenRollup, GenRollup).Sample((a, b, c) => Rollup.Merge(Rollup.Merge(a, b), c).Equivalent(Rollup.Merge(a, Rollup.Merge(b, c))));
   ```
4. **FakeTimeProvider + TickBatcher**
   ```csharp
   var time = new FakeTimeProvider(Start); var b = new TickBatcher(time);
   b.Add(sample); time.Advance(TimeSpan.FromSeconds(10)); Assert.Single(b.Drain());
   ```
5. **Bogus seeding (dev endpoint and perf fixtures)**
   ```csharp
   var ticks = new Faker<Tick>().UseSeed(42).RuleFor(t => t.MotionMean, f => f.Random.Double(0, .3)).RuleFor(t => t.Persons, f => f.Random.Int(0, 3)).Generate(8_640);
   ```
6. **Verify snapshot of the template recap**
   ```csharp
   [Fact] public Task TemplateRecapIsStable() => Verify(TemplateRecap.Build(FixtureDay()));
   ```
7. **M.E.AI provider selection (Infrastructure DI)**
   ```csharp
   services.AddChatClient(sp => opts.Provider switch {
       AiProvider.Ollama => new OllamaApiClient(new Uri(opts.Ollama.Endpoint), opts.Ollama.Model),
       _ => new AzureOpenAIClient(new Uri(opts.AzureOpenAi.Endpoint), new DefaultAzureCredential()).GetChatClient(opts.AzureOpenAi.Deployment).AsIChatClient() });
   var text = (await chat.GetResponseAsync(prompt, cancellationToken: ct)).Text;
   ```
8. **SignalR push, trim-safe client**
   ```csharp
   app.MapHub<StatsHub>("/hubs/stats").RequireAuthorization();
   await hub.Clients.User(userId).SendAsync("rollup", delta, ct);
   // client
   new HubConnectionBuilder().WithUrl(nav.ToAbsoluteUri("/hubs/stats")).WithAutomaticReconnect()
     .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, PoWatchJsonContext.Default)).Build();
   ```
9. **HybridCache with tag invalidation on ingest**
   ```csharp
   await cache.GetOrCreateAsync($"stats:{user}:{family}:{range}", ct => ComputeAsync(ct), tags: [$"u:{user}"], cancellationToken: ct);
   await cache.RemoveByTagAsync($"u:{user}", ct); // after a batch lands
   ```
10. **Radzen mission-control widgets + Humanizer (server-formatted labels)**
    ```razor
    <RadzenArcGauge><RadzenArcGaugeScale Min="0" Max="100"><RadzenArcGaugeScaleValuePointer Value="@Occupancy" /></RadzenArcGaugeScale></RadzenArcGauge>
    <RadzenChart><RadzenAreaSeries Data="@Energy" CategoryProperty="At" ValueProperty="Motion" /></RadzenChart>
    ```
    `stats.LongestEmpty.Humanize(2)` gives "3 hours, 12 minutes", and `frames.ToMetric()` gives "12.4k".

### D0 findings — background tabs (design note, not measured)

Could not be measured here: headless Chromium does not reproduce hidden-tab throttling. Decisions
are based on documented Chromium behaviour:

- `requestAnimationFrame` and `requestVideoFrameCallback` stop in a hidden tab, and main-thread
  timers are throttled (down to once a minute after five minutes hidden).
- Dedicated workers are not subject to that intensive throttling.
- So the sampling clock lives in a small worker, which posts "sample now" to the page. The page grabs
  the frame from the `<video>` element. The detector already runs in a worker.
- The page requests a Screen Wake Lock while a session runs, so an unattended laptop does not
  sleep the display.
- Every tick carries its real sample counts, so throttled stretches show up as low-rate ticks or gaps
  instead of fake zeros. The Pipeline panel shows the achieved Hz.
- To verify on real hardware in Phase 5: start a session, hide the tab for 10 minutes, and compare
  the pixel Hz before and after in the Pipeline panel.
