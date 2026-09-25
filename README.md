# PoWatch

PoWatch is a fun, stat-heavy webcam observer. Point a camera at anything, press Start, walk away, and
come back to a nerdy statistical picture of what happened: presence, motion, space, objects, recurring
"regulars", time patterns, anomalies, environment, captions, achievements, and pipeline telemetry. The
app runs as a hosted Blazor WebAssembly client served by `PoWatch.Api`; the API owns authentication,
storage, telemetry, diagnostics, and vertical feature endpoints.

The browser runs the sensing locally (pixel metrics, an object detector, and a small vision-language
model) against the device camera. Frames never leave the device except chosen highlight snapshots.
Compact 10-second ticks are posted through the BFF boundary, stored in Azure Table Storage (Azurite
locally), and rolled up into minute/hour/day/all-time stats.

> The app is mid-pivot from its earlier caregiver-monitor form. `SPEC.md` describes the target state;
> `tasks/todo.md` tracks progress.

The project targets .NET 10 from `global.json` and uses central package management. The client and shared DTO assembly are trim-analyzer clean, using source-generated JSON metadata instead of reflection-heavy serialization. Authentication is BFF-style: Microsoft Entra ID or dev/test guest sign-in creates an encrypted HttpOnly cookie, while the WASM client only asks `/auth/me` for state.

## Local Setup

Run the setup script from the repository root:

```powershell
.\SCRIPTS\setup.ps1
```

The script prepares local tooling, starts Azurite through Docker Compose, and walks the Azure login path when needed. The development API listens on `http://localhost` (port 80, set by `PoWatch:Ports:Http` in
`appsettings.Development.json`) and `https://localhost:5001`. If port 80 is taken, the dev port
negotiator falls back to the next free port and logs it.

Useful commands:

```powershell
dotnet restore PoWatch.slnx
dotnet build PoWatch.slnx
dotnet test PoWatch.slnx
dotnet run --project src/PoWatch.Api/PoWatch.Api.csproj
```

Test suites have maximums of 100 unit, 50 integration, 25 API E2E, and 25 UI E2E cases.
After building, run `./SCRIPTS/check-test-caps.ps1` and `./SCRIPTS/check-hygiene.ps1`.
For isolated UI verification, install Chromium using the suite's generated `playwright.ps1`, set
`E2E_LOCAL=1`, and run `dotnet test tests/PoWatch.E2EUI -c Release`.

Handoff synthesis uses `AiProvider:Provider`: `Template` (default), `AzureOpenAi`, or `Ollama`.
External providers fall back to the template when unavailable. Production resource names are shared
between Bicep and CI in `infra/deployment.json`.

## Documentation

- `AGENTS.md` — rules for any agent (or human) working in this repo.
- `SPEC.md` — objective, journeys, stat catalog, stack, boundaries, success criteria.
- `CAPABILITY-MAP.md` — which project owns which capability, and what each old subsystem became.
- `tasks/plan.md` and `tasks/todo.md` — architecture decisions, risks, and the task checklist.

This `README.md` stays at the level of "what is this app and how do I run it".

[Cleanup decisions and validation](docs/cleanup.md) document the reduced feature surface.
