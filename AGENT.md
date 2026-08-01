# AGENT.md — PoWatch

Operating rules for any agent or contributor working in this repository. These are enforced by the
build and CI where possible; where they are not, treat them as binding anyway.

PoWatch is a calm, local-first room-monitoring app: a Blazor WebAssembly client runs vision
inference on-device (WebGPU / transformers.js) and posts observations to a .NET Minimal API that
persists them to Azure Table + Blob Storage.

---

## 1. Core principles

- **Naming.** Solution, projects, and root namespaces use the `Po{Name}` prefix — here, `PoWatch`.
- **Stack.** .NET 10 / latest C#. Every dependency version lives in `/Directory.Packages.props`;
  a `PackageReference` in a `.csproj` must never carry a `Version` attribute.
- **Compiler contract.** `Directory.Build.props` applies `Nullable`, `TreatWarningsAsErrors`,
  `AnalysisMode=Recommended`, and `EnforceCodeStyleInBuild` to every project. **The build must stay
  at zero warnings.** Do not silence an analyzer inline; if a rule genuinely does not fit, disable
  it in `.editorconfig` with a comment stating why (see the existing entries for the format).
  - `TargetFramework` deliberately stays in each `.csproj`. Setting it centrally is evaluated too
    early for SDK framework inference and breaks trimming in the Blazor WASM and Shared projects.
- **Git.** Trunk-based on `master`. No feature branches unless explicitly requested.
- **Domain integrity.** No primitive obsession, no magic strings. Identifiers are
  `readonly record struct` types (`SubjectId`, `ObservationEventId`) and states are enums
  (`IdentityStatus`, `AlertMetric`).
  - Conversions between an id and its underlying `string`/`Guid` are **explicit** — `SubjectId.From`,
    `ObservationEventId.Parse`, or a cast. This is the point of the type: while the conversion was
    implicit, any string in scope silently satisfied a `SubjectId` parameter.
  - Adopt raw values at the edges only: persistence reads, DTO mapping, transport parsing.

---

## 2. Layout

```
/
├── AGENT.md
├── Directory.Build.props          # compiler contract
├── Directory.Packages.props       # central package versions
├── SCRIPTS/
├── infra/                         # Bicep — resource groups PoShared / PoWatch
├── src/
│   ├── PoWatch.Api/               # Minimal API, BFF host, feature slices; serves the WASM client
│   ├── PoWatch.Application/       # Contracts, options, business services
│   ├── PoWatch.Client/            # Blazor WASM UI
│   ├── PoWatch.Domain/            # Entities, strongly-typed ids, enums
│   ├── PoWatch.Infrastructure/    # Azure persistence, runtime adapters
│   └── PoWatch.Shared/            # DTOs shared across the BFF boundary
└── tests/
    ├── PoWatch.Unit/              # Pure logic, No-I/O
    ├── PoWatch.Integration/       # Azurite via Testcontainers
    ├── PoWatch.E2EAPI/            # API contract
    └── PoWatch.E2EUI/             # Playwright
```

Directory depth stays shallow — at most two levels inside a project.

> **Known deviation.** The reference layout is a three-project `src/` (`API` / `Client` / `Shared`).
> This repo additionally has `Domain`, `Application`, and `Infrastructure`. Collapsing them into the
> feature slices is tracked work, not a licence to add a fourth layer.

### Vertical slices

- API endpoints, their request/response handling, and their wiring live together under
  `PoWatch.Api/Features/{FeatureName}`.
- **Slices must not reference each other.** Only `Program.cs`, as the composition root, may reference
  them all. Anything two slices need belongs in `PoWatch.Shared` (DTOs) or `PoWatch.Application`.
- `ArchitectureBoundaryTests` in `PoWatch.Unit` enforces the project-level direction of dependencies
  by reflecting over the compiled reference set. If you invert a boundary, that test fails.

---

## 3. API, security, diagnostics

- Map endpoints with `IEndpointRouteBuilder` + `MapGroup()`. Document via
  `Microsoft.AspNetCore.OpenApi`; the Scalar UI is at `/scalar/v1`.
- **`/health`** serves two audiences from one route:
  - machine clients (App Service probe, CI deploy gate, `curl`) get the JSON document;
  - a browser (`Accept: text/html`) gets the Blazor **Health** page listing every connection.
  - The rewrite that makes this work runs *before* `app.UseRouting()`, which is why `UseRouting` is
    called explicitly in `Program.cs`. Move it and the page silently reverts to JSON.
  - **The JSON contract gates every production deploy — do not change its shape.**
- **`/diag`** returns masked environment keys and integration status. `/diag/boot` reports the last
  startup milestone and dependency readiness — the first thing to check on a 500.30.
  Secret values must always be masked.
### Authentication (BFF)

- Server-managed encrypted **HttpOnly, SameSite=Strict, Secure** cookie (`PoWatch.Auth`). The WASM
  client never holds tokens; it derives state from `/auth/me` via `BffAuthenticationStateProvider`.
- Microsoft Entra OIDC on `/common` (`AzureAd` config), wired only when `AzureAd:ClientId` is set;
  the issuer is validated against `AzureAd:AllowedTenants`.
- Guest bypass: cookie sign-in via `/auth/login/fake` plus the header-driven `FakeAuthHandler`
  (`X-Fake-User` / `X-Fake-Roles`) for tests. **It throws if enabled in Production**
  (`AuthenticationSetup`) — never weaken that guard.
- Routes: `/auth/me,config,login/microsoft,login/fake,logout`. Client uses `AuthorizeRouteView` and
  `[Authorize]` pages; `/login` is anonymous and env-aware from `/auth/config`.
- **Server authorization is default-deny.** Both `SetDefaultPolicy` and `SetFallbackPolicy` require
  an authenticated user, so an endpoint that omits `.RequireAuthorization()` is still protected.
  The explicit `.AllowAnonymous()` opt-outs are: the SPA host page (`MapFallbackToFile`), static
  assets (`MapStaticAssets`), `/health`, `/diag`, and the `/auth` group. Dev/Test add the FakeAuth
  scheme to the policy so the guest bypass satisfies it.
- Environment matrix: Prod → Microsoft only; Dev → Microsoft + guest; Test → guest bypass.

### Observability

- OpenTelemetry → Azure Monitor; `cloud_RoleName` is mapped to the entry assembly by reflection.
- **HybridCache** (~10 s) fronts the frequently-polled `/api/identity/subjects/live-status`.
- Azure OpenAI uses a typed `HttpClient` with `AddStandardResilienceHandler`.

---

## 4. UI / Blazor

- Header layout contract: **left** branding · **centre** navigation actions · **right** mock-data
  chip, session, theme toggle, sign-out.
- When mock inference is active, the persistent **MOCK DATA** chip must remain visible.
- **No inline styles.** Use scoped `.razor.css` plus the design tokens in `wwwroot/css/tokens.css`.
  Never hard-code a colour — every value comes from a CSS custom property.
- Light and dark themes are both first-class, driven by `:root[data-theme="…"]`.
- Stable selectors for tests are `data-test` attributes (Playwright's test-id attribute is
  configured to match).
- The **MOCK DATA** chip is driven by any active `IMockable` service.

### Local-first AI inference

- **Single model registry.** `wwwroot/model-registry.json` is the one source of truth for the VLM
  list. The inference Web Worker reads it (id / modelClass / dtype-fallback chain) and the C# model
  picker reads it (key / label). Never duplicate the model list in both JS and C#.
- **Pinned, self-hosted supply chain.** transformers.js **3.8.1** and its ONNX Runtime wasm are
  vendored under `wwwroot/lib/transformers-3.8.1/` and loaded from our own origin — no live CDN
  `import()`. To upgrade: vendor a new dist folder and bump `_TRANSFORMERS_VERSION` in
  `inference-worker.js`. (Model *weights* are still fetched from the HF hub on first use — inherent
  to a browser VLM.)
- **The worker's quality gates can silently starve the pipeline.** `inference-worker.js` rejects
  model replies that are unstructured, echo the prompt, are too short/repetitive, or end mid-clause.
  Every rejection returns `isAvailable: false`, which `ObserverHub` treats as a skip — so nothing is
  ingested and Room Activity stays empty while the loop *looks* healthy. Each rejection now carries
  `rawOutput`, and Live Room raises a banner once 3+ cycles run with zero structured replies.
  Before adding a sixth gate, check the skip rate: filters here compound.

---

## 5. Testing, CI/CD, hygiene

- **Targets: 100 unit · 50 integration · 25 API E2E · 25 UI E2E.**
  Current counts are well below this (67 · 24 · 4 · 1) — closing that gap is the single largest
  outstanding item. Add tests with the feature you are writing.
- CI (`.github/workflows/deploy.yml`) restores, builds, verifies formatting, then runs all four
  suites before publishing. The `emergency` workflow-dispatch input skips the formatting and test
  gates — it exists solely so a red test cannot trap a hotfix during an outage. Do not use it
  routinely.
- `PoWatch.E2EUI` self-skips unless `E2E_BASE_URL` is set, so it is a no-op against a headless build.
- Azure: resources live in resource groups **`PoShared`** (shared platform services) and
  **`PoWatch`**. Authenticate with system-assigned Managed Identity + Key Vault.
  **No raw connection strings in app settings.**
  - Bicep cannot rename an existing resource group; changing a name provisions a new one.
- Purge dead code and orphaned assets as you go.

### Things that have broken production before

Read these before changing the related code:

- **Do not set `IsTrimmable` on `PoWatch.Client`.** Routable `@page` components are discovered by
  reflection; member-level trimming deletes every page and the router 404s all routes.
- **Do not make the App Service runtime-stack check a hard deploy gate.** `netFrameworkVersion`
  reads `v4.0` on Windows App Service even for healthy .NET 10 apps.
- **Formatting and test gates must stay skippable** via the `emergency` input.
- Culture-sensitive formatting is a correctness issue here, not style: Table Storage `PartitionKey`s
  are `DateOnly.ToString("yyyyMMdd", CultureInfo.InvariantCulture)`. Under a non-Gregorian calendar
  culture an unqualified `ToString` writes to the wrong partition. Pin persistence and log
  formatting to `InvariantCulture`; use `CurrentCulture` only for operator-facing display.

---

## 6. Local development

```bash
dotnet build PoWatch.slnx -c Release          # must end with 0 warnings, 0 errors
dotnet format PoWatch.slnx                    # before committing
dotnet test  tests/PoWatch.Unit/PoWatch.Unit.csproj -c Release
dotnet test  tests/PoWatch.Integration/PoWatch.Integration.csproj -c Release   # needs Docker
dotnet run   --project src/PoWatch.Api        # serves API + WASM client on one origin
```

The API hosts the client, so there is one process and no CORS. Integration tests need Docker for
the Azurite container.

Local ports are HTTP `5000` / HTTPS `5001`; `PortNegotiation` rebinds automatically (commonly to
`5002`/`5003`) when a stale process holds them. Azurite runs as the `PoWatch` container via
`docker-compose.yml`. `SCRIPTS/setup.ps1` cold-starts the toolchain, Azurite, and `az login`.
