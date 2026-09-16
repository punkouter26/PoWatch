# PoWatch

PoWatch is a mobile-first room observation system for caregivers who need a calm, same-origin web app that can watch for meaningful activity, preserve daily context, and support shift handoff without exposing browser-held tokens. The app runs as a hosted Blazor WebAssembly client served by `PoWatch.Api`; the API owns authentication, storage, telemetry, diagnostics, and vertical feature endpoints.

Users start in the Observer Hub, select a local vision model, and run a browser-side inference loop against the device camera. Inferred observations are posted through the BFF boundary to server-validated Minimal API slices, written to Azure Table Storage or Azurite locally, and shown back through live timelines, subject identity management, archives, handoff reports, and diagnostics.

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

Test suites have maximums of 100 unit, 50 integration, 25 API E2E, and 25 UI E2E cases.
After building, run `./SCRIPTS/check-test-caps.ps1` and `./SCRIPTS/check-hygiene.ps1`.
For isolated UI verification, install Chromium using the suite's generated `playwright.ps1`, set
`E2E_LOCAL=1`, and run `dotnet test tests/PoWatch.E2EUI -c Release`.

Handoff synthesis uses `AiProvider:Provider`: `Template` (default), `AzureOpenAi`, or `Ollama`.
External providers fall back to the template when unavailable. Production resource names are shared
between Bicep and CI in `infra/deployment.json`.

## Documentation

`AGENT.md` is the operating manual for this repo — compiler contract, vertical-slice boundaries,
BFF auth, telemetry, the inference worker's quality gates, and the list of things that have broken
production before. It is binding for autonomous agents and human contributors alike. This `README.md`
stays at the level of "what is this app and how do I run it".

[Cleanup decisions and validation](docs/cleanup.md) document the reduced feature surface.
