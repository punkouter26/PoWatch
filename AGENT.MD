# AGENT.MD — PoWatch Context Layer

Authoritative map of architectural boundaries and project configuration. Update on any change to boundaries, projects, or environment wiring.

## Solution
- Prefix `Po`. Solution `PoWatch.slnx`. Target **.NET 10** via `global.json` (`10.0.301`, `rollForward: latestFeature`).
- CPM via root `Directory.Packages.props`; shared build config in `Directory.Build.props` (`Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`).
- Line endings normalized to LF via `.gitattributes` (matches `.editorconfig`; keeps `dotnet format` identical on Windows and CI).

## Projects (`/src`)
- `PoWatch.Api` — host; serves the Blazor WASM client from the same origin (no CORS). **Vertical feature slices** live under `Features/<Feature>/` (Auth, Observer, Archives, Identity, Fhir, Diagnostics) — each slice owns its `Map<Feature>Feature` endpoints directly (the old `Endpoints/` + `*FeatureRoutes` indirection was removed). Also: BFF auth, Key Vault, health checks, telemetry, rate limiting, global ProblemDetails.
- `PoWatch.Client` — Blazor WASM UI (Radzen). WebGPU/mock inference; BFF auth (forced login).
- `PoWatch.Shared` — shared DTOs + shared feature flags.
- `PoWatch.Application` — service/contract layer. `PoWatch.Domain` — domain models. `PoWatch.Infrastructure` — Azure Table/Blob, Key Vault, typed Azure OpenAI HttpClient.

> NOTE: cross-project layout is still Clean-style (Domain/Application/Infrastructure). The API layer is VSA-aligned; a full cross-project slice collapse remains an open item.

## Authentication (BFF — rule 4)
- Server-managed session in an encrypted **HttpOnly, SameSite=Strict, Secure** cookie (`PoWatch.Auth`). The WASM client never holds tokens; it derives state from `/auth/me` (`BffAuthenticationStateProvider`).
- Interactive sign-in via **Microsoft Entra OIDC** on `/common` (`AzureAd` config section), wired only when `AzureAd:ClientId` is set; shape-based issuer validation against `AzureAd:AllowedTenants`.
- Guest/dev bypass: cookie sign-in via `/auth/login/fake` + header-based `FakeAuthHandler` (`X-Fake-User` / `X-Fake-Roles`) for automated tests. Throws if enabled in Production.
- Routes: `/auth/me`, `/auth/config`, `/auth/login/microsoft`, `/auth/login/fake`, `/auth/logout`.
- Client forces login: `App.razor` uses `CascadingAuthenticationState` + `AuthorizeRouteView`; protected pages carry `@attribute [Authorize]`; `/login` is anonymous and renders env-appropriate buttons from `/auth/config`.
- **Env matrix**: Prod → Microsoft only. Dev → Microsoft + guest. Test → guest bypass (`ASPNETCORE_ENVIRONMENT=Test`, `appsettings.Test.json`).

## Observability
- OpenTelemetry → Azure Monitor; `cloud_RoleName` mapped to the entry assembly via reflection (rule 6.2). Serilog to console/file.
- Hot-path ingest logging uses source-generated `[LoggerMessage]` (`ObservationServiceLog`).
- "USING MOCK DATA" banner shows in the navbar when any `IMockable` service is active (rule 6.5).

## Diagnostics
- `/health` (JSON health checks) and `/diag` (masked env keys + integration statuses) — both UI-less server routes.

## Resilience
- Azure OpenAI uses a **typed HttpClient** with `AddStandardResilienceHandler` (retry + circuit breaker + timeout + rate limiter).

## Tests (`/tests`) — four C# projects (rule 2.2)
- `PoWatch.UnitTests` — xUnit, isolated (57).
- `PoWatch.IntegrationTests` — xUnit + Testcontainers Azurite; runs under `Test` env (24).
- `PoWatch.E2EApi` — pure API client→server flows over Azurite, incl. BFF auth flow (4).
- `PoWatch.E2EUI` — C# Playwright; drives a live instance via `E2E_BASE_URL` (skips when unset).
- `PoWatch.E2E` (TypeScript Playwright) remains as an auxiliary suite; not in `PoWatch.slnx`.

## CI/CD
- `.github/workflows/deploy.yml` → restore, build, `dotnet format --verify`, publish, deploy to Azure App Service `app-powatch` (RG `PoWatch`). **Tests are not run in the pipeline (rule 6.4)** — run locally/pre-merge.

## Local run
- Ports HTTP `5000` / HTTPS `5001` (`launchSettings.json`).

## Notable config changes (this pass)
- `global.json` SDK `10.0.202`→`10.0.301` / `latestFeature` (installed-band mismatch broke first-run build).
- Pinned `Microsoft.OpenApi` `2.7.5` (GHSA-v5pm-xwqc-g5wc). Bumped `Microsoft.Extensions.*` to `10.0.9` (required by `Microsoft.Extensions.Http.Resilience 10.7.0`).
- Added packages: `Microsoft.Extensions.Http.Resilience`, `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.AspNetCore.Components.Authorization`, `Microsoft.Playwright`.
