# PoWatch

PoWatch is a mobile-first room observation system for caregivers who need a calm, same-origin web app that can watch for meaningful activity, preserve daily context, and support shift handoff without exposing browser-held tokens. The app runs as a hosted Blazor WebAssembly client served by `PoWatch.Api`; the API owns authentication, storage, telemetry, diagnostics, and vertical feature endpoints.

Users start in the Observer Hub, select a local vision model, and run a browser-side inference loop against the device camera. Inferred observations are posted through the BFF boundary to server-validated Minimal API slices, written to Azure Table Storage or Azurite locally, and shown back through live timelines, subject identity management, archives, handoff reports, diagnostics, and optional FHIR export.

The project targets .NET 10 from `global.json` and uses central package management. The client and shared DTO assembly are trim-analyzer clean, using source-generated JSON metadata instead of reflection-heavy serialization. Authentication is BFF-style: Microsoft Entra ID or dev/test guest sign-in creates an encrypted HttpOnly cookie, while the WASM client only asks `/auth/me` for state.

## Local Setup

Run the setup script from the repository root:

```powershell
.\SCRIPTS\setup.ps1
```

The script prepares local tooling, starts Azurite through Docker Compose, and walks the Azure login path when needed. The development API listens on `http://localhost:5000` and `https://localhost:5001` when configured.

Useful commands:

```powershell
dotnet restore PoWatch.slnx
dotnet build PoWatch.slnx
dotnet test PoWatch.slnx
dotnet run --project src/PoWatch.Api/PoWatch.Api.csproj
```

## Documentation

Generated reports live in `docs/`. Each one has a reading-depth switch — **Very basic** (30 seconds), **Basic** (the important parts), **Complete** (full implementation detail) — that also swaps the embedded diagram for a matching level of detail.

| Report | Covers |
| --- | --- |
| [`docs/ARCHITECTURE_REPORT.html`](docs/ARCHITECTURE_REPORT.html) | C4 L1–L3, vertical-slice boundaries (`@page` → `MapGroup`), middleware ordering, and why the static-asset routes opt out of the default-deny policy |
| [`docs/AI_SERVICES_REPORT.html`](docs/AI_SERVICES_REPORT.html) | Every model-executing path, model/version and fallback matrices, parameters, triggers, cost model, and the measurement gaps |
| [`docs/ROLES_PERMISSIONS_MATRIX.html`](docs/ROLES_PERMISSIONS_MATRIX.html) | Interactive Principal × Environment access grid, plus every endpoint that carries no authorization check |
| [`docs/USER_WORKFLOW.html`](docs/USER_WORKFLOW.html) | One observation traced UI → API → middleware → orchestrator → providers → Table/Blob storage, with failure modes and status banners |
| [`docs/VISUAL_ARCHITECTURE_DASHBOARD.html`](docs/VISUAL_ARCHITECTURE_DASHBOARD.html) | Single-page synthesis — embedded C4, three pipeline sequences, an ERD, and plain-English narrative cards paired with each visual |

All five reports share the three-tier reading model — Executive (30 s), Architectural, Implementation — and the same design language. Visual standards follow the [cathrynlavery/diagram-design](https://github.com/cathrynlavery/diagram-design) conventions: clearly bordered tiered sections, a sticky reading switch, paired data tables next to every chart, and inline Mermaid blocks rendered from the source files in `docs/diagrams/`.

Diagram sources are Mermaid files in `docs/diagrams/`, compiled to SVG in `docs/assets/`. The Visual Architecture Dashboard additionally embeds interactive Mermaid directly in the page, so the dashboard loads a single CDN script and renders C4, sequences and the ERD at runtime:

```powershell
npx @mermaid-js/mermaid-cli -i docs/diagrams/architecture_flow_complete.mmd -o docs/assets/architecture_flow_complete.svg -b transparent
```

Deployment runbooks live in `docs/technical/`. `AGENT.md` remains the operational context layer for autonomous coding agents.
