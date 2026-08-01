# AGENT.MD — PoWatch Context Layer

Authoritative map of architectural boundaries and project configuration. Update on any change to boundaries, projects, or environment wiring.

## Solution
- Prefix `Po`. Solution `PoWatch.slnx`. Target **.NET 10** via `global.json` (`10.0.301`, `rollForward: latestFeature`).
- CPM via root `Directory.Packages.props`; shared build config in `Directory.Build.props` (`Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` — all global).
- LF line endings enforced via `.gitattributes` (matches `.editorconfig`).

## Retained root layout (rule 7)
`/src`, `/tests`, `/infra` (Bicep IaC — App Service Plan, Key Vault, storage, observability), `/docs` (PRD_Master, Mermaid + SVG diagrams, `technical/deployment-guidelines.md`), `/SCRIPTS` (`setup.ps1`, `restore-prod-identity-and-monitoring.ps1`), `.vscode/{launch,tasks,settings}.json`, `docker-compose.yml` (local Azurite), `README.md`, the CPM/build props, `global.json`, and `AGENT.MD`. `azure.yaml` and Copilot configs are not used.

## Projects (`/src`)
- `PoWatch.Api` — host; serves the Blazor WASM client same-origin (no CORS). **Vertical feature slices** under `Features/<Feature>/` (Auth, Observer, Archives, Identity, Fhir, Diagnostics), each owning its `Map<Feature>Feature` endpoints. BFF auth, Key Vault, health checks, telemetry, rate limiting, ProblemDetails, HybridCache.
- `PoWatch.Client` — Blazor WASM (Radzen). Trim-safe: `IsTrimmable` + `EnableTrimAnalyzer` pass clean via a source-generated `PoWatchJsonContext` (all BFF DTOs) and `EnableConfigurationBindingGenerator`. Forced-login BFF auth.
- `PoWatch.Shared` — DTOs + shared flags; `IsTrimmable` + `EnableTrimAnalyzer`.
- `PoWatch.Application` / `PoWatch.Domain` / `PoWatch.Infrastructure` — service/contract, domain, and Azure/Key-Vault layers.

> Slices depend only on shared Application contracts (e.g. `IObservationRepository`), not on each other's services. The Observer SSE stream reads the repository directly rather than `ArchivesService`.

## Authentication (BFF — rule 4)
- Server-managed encrypted **HttpOnly, SameSite=Strict, Secure** cookie (`PoWatch.Auth`); WASM never holds tokens, derives state from `/auth/me` (`BffAuthenticationStateProvider`).
- Microsoft Entra OIDC on `/common` (`AzureAd` config), wired only when `AzureAd:ClientId` is set; issuer validated against `AzureAd:AllowedTenants`.
- Guest bypass: cookie sign-in via `/auth/login/fake` + header `FakeAuthHandler` (`X-Fake-User`/`X-Fake-Roles`) for tests; throws in Production.
- Routes: `/auth/me,config,login/microsoft,login/fake,logout`. Client: `AuthorizeRouteView` + `[Authorize]` pages; `/login` anonymous, env-aware from `/auth/config`.
- **Server authz is default-deny (rule 4.5):** both `SetDefaultPolicy` and `SetFallbackPolicy` require an authenticated user, so any endpoint that omits `.RequireAuthorization()` is still protected. Explicit `.AllowAnonymous()` opt-outs: SPA host page (`MapFallbackToFile`), static assets (`MapStaticAssets`), `/health`, `/diag`, and the `/auth` group. Dev/Test add the FakeAuth scheme to the policy so the guest bypass satisfies it.
- Env matrix: Prod → Microsoft only; Dev → Microsoft + guest; Test → guest bypass.

## Observability & performance
- OpenTelemetry → Azure Monitor; `cloud_RoleName` mapped to the entry assembly via reflection. Hot-path ingest uses source-generated `[LoggerMessage]`.
- **HybridCache** (~10s) fronts the frequently-polled `/api/identity/subjects/live-status`.
- Azure OpenAI: typed HttpClient + `AddStandardResilienceHandler`.
- "USING MOCK DATA" banner shows when any `IMockable` service is active.

## Local-first AI inference (rule §7, 1.5)
- **Single model registry:** `wwwroot/model-registry.json` is the one source of truth for the VLM list. The inference Web Worker reads it (id/modelClass/dtype-fallback chain) and the C# picker reads it (key/label) — no duplicated model list in JS+C#.
- **Pinned, self-hosted supply chain:** transformers.js **3.8.1** and its ONNX Runtime wasm are vendored under `wwwroot/lib/transformers/3.8.1/` and loaded from our own origin (no live CDN `import()`). ORT wasm path is pinned to that dir. Upgrade = vendor a new dist folder + bump `_TRANSFORMERS_VERSION` in `inference-worker.js`. (Model *weights* are still fetched from HF hub on first use — inherent to a browser VLM.)

## Diagnostics
- `/health` (JSON checks) and `/diag` (masked env + integration statuses) — server-owned; the client Diagnostics page is `/diagnostics` only.

## Tests (`/tests`) — four projects (rule 2.2)
- `PoWatch.UnitTests` — isolated, pure unit tests, no infrastructure (57).
- `PoWatch.IntegrationTests` — infrastructure tests against ephemeral Azurite via Testcontainers; run under `Test` env (24).
- `PoWatch.E2EAPI` — pure API client→server flows against real Azurite, incl. BFF guest-auth flow (4).
- `PoWatch.E2EUI` — C# Playwright UI; drives a running instance via `E2E_BASE_URL`, skips when unset.

## CI/CD & local
- `.github/workflows/deploy.yml` — only workflow: restore, build, format-verify, publish, deploy to App Service `app-powatch-win` (RG `PoWatch`), then a `/health` gate. **No tests in the pipeline (rule 6.4).**
- Local: HTTP `5000` / HTTPS `5001`; Azurite container `PoWatch` (docker-compose); `SCRIPTS/setup.ps1` cold-starts toolchain + Azurite + az login.
