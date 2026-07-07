# PoWatch PRD Master

## Source Of Truth

This document is the durable product and architecture source for PoWatch. It aligns the application goal, vertical slice boundaries, endpoint surface, trimmer rules, and observability standards used by the codebase.

## Product Goal

PoWatch helps caregivers monitor a room from a mobile portrait web interface, capture local AI observations, identify subjects over time, and generate reliable archives or handoff artifacts. The app prioritizes privacy, operator clarity, and local-first resilience.

## Primary Users

- Caregiver or shift operator: starts monitoring, reviews events, acknowledges significant moments, and exports handoff context.
- Administrator or developer: configures feature flags, authentication, storage, diagnostics, and optional integrations.
- Clinical or integration reviewer: inspects archives, FHIR output, and system diagnostics.

## System Boundaries

- `PoWatch.Api`: same-origin host for the Blazor client, BFF authentication, Minimal API vertical slices, health, diagnostics, OpenAPI, rate limiting, ProblemDetails, telemetry, and storage-backed handlers.
- `PoWatch.Client`: Blazor WASM mobile-first UI with Radzen components, local inference worker, typed API client, source-generated JSON context, and custom authentication state provider.
- `PoWatch.Shared`: DTOs and shared feature flag types only. No server service abstractions or infrastructure dependencies.
- `PoWatch.Application`: service contracts, business services, options, and cross-slice application logic.
- `PoWatch.Domain`: domain models and pure domain services.
- `PoWatch.Infrastructure`: Azure Table, Blob, Key Vault, diagnostics, acknowledgement registry, and Azure OpenAI implementations.

## Vertical Slice Boundaries

Each API feature owns its route mapping under `src/PoWatch.Api/Features/<Feature>/` and depends on application contracts instead of sibling feature services.

| Slice | Route Surface | Owner Responsibilities |
| --- | --- | --- |
| Auth | `/auth/me`, `/auth/config`, `/auth/login/microsoft`, `/auth/login/fake`, `/auth/logout` | BFF session state, Entra challenge, guest bypass, local return URL validation |
| Observer | `/api/observer/ingest`, `/api/observer/state`, `/api/observer/events`, `/api/observer/acknowledge` | Observation ingest, runtime state, SSE event streaming, acknowledgement |
| Identity | `/api/identity/subjects`, `/api/identity/merge`, `/api/identity/subjects/live-status`, `/api/identity/subjects/live-risk`, `/api/identity/subjects/{id}/baseline` | Subject registry, rename, merge, cached live status, drift baseline |
| Archives | `/api/archives/{date}`, `/api/archives/{date}/handoff-report`, `/api/archives/{date}/handoff-brief`, `/api/blobs/*` | Daily chapters, PDF handoff, Handoff Coach, significant image access |
| Diagnostics | `/api/diagnostics/status`, `/api/diagnostics/reset`, `/diag`, `/diag/boot`, `/health` | Masked system state, guarded reset, startup readiness, dependency health |
| FHIR | `/fhir/Observation`, `/fhir/Observation/{id}` | Feature-flagged FHIR R4 Observation export |

## Authentication Requirements

- Browser-held access tokens are prohibited.
- WASM derives identity from `/auth/me` through `BffAuthenticationStateProvider`.
- Auth cookie must remain encrypted, HttpOnly, SameSite Strict, and Secure in deployed environments.
- Production exposes Microsoft sign-in only when Entra configuration is present.
- Dev and test can use guest bypass when `DeveloperBypassAuth` is enabled; the fake handler must not work in Production.
- API authorization is default-deny through default and fallback policies. Anonymous access is explicit for SPA boot, static assets, auth endpoints, health, and diagnostics boot probes.

## Trimmer-Compatible Model Criteria

Projects that run in the browser or share DTOs with the browser must stay trim-analyzer clean.

- `PoWatch.Client` and `PoWatch.Shared` keep `IsTrimmable` and `EnableTrimAnalyzer` enabled.
- JSON serialization uses `PoWatchJsonContext`; new request and response DTOs must be added to the source generation context before browser use.
- Configuration binding should use generated binding where supported.
- Avoid runtime reflection, dynamically discovered members, unbounded polymorphic serialization, and linker-hostile APIs in browser-facing paths.
- New DTOs should be public, sealed where practical, simple property bags, and free of behavior or service dependencies.

## Source-Generated Logging Standard

Hot paths must use source-generated logging rather than interpolated strings or allocation-heavy logging helpers.

- Use `[LoggerMessage]` partial methods for high-frequency ingest, stream, storage, and telemetry events.
- Keep event names stable and structured property names explicit.
- Never log secrets, tokens, raw connection strings, raw image payloads, or direct personal contact details.
- Include correlation or trace context where it helps operations.

## Data Model

Azure Table Storage is the durable store, with Azurite used locally.

- Observations table: partitioned by UTC date `yyyyMMdd`; row key starts with subject id and observation id for idempotent ingest. Current rewrite code can create inverted-tick row keys during identity history rewrite, so readers sort by `ObservedAtUtc`.
- Subjects table: partition key `Subjects`; row key `SubjectId`; profile columns store display name, identity status, first seen, last seen, and recent activity metadata.
- Blob storage: significant images are addressed by subject/date paths and exposed only through scoped SAS descriptors.

## Nonfunctional Requirements

- Same-origin hosting; no CORS dependency.
- Mobile portrait path is primary.
- Local inference must load vendored transformers.js from app origin.
- Azure OpenAI use must go through typed `HttpClient` with standard resilience.
- Frequently polled live identity status should remain cache-fronted with short HybridCache expiration.
- Health and boot diagnostics must avoid secrets and stay dependency-light.
