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

Architecture details, route maps, data schemas, and flow diagrams live in `docs/`. `AGENT.MD` remains the operational context layer for autonomous coding agents.
