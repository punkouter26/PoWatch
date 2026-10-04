# CLAUDE.md — PoWatch

@AGENTS.md

`AGENTS.md` above holds the rules. `docs/README.md` is the project overview (pipeline, pages, API,
storage); read it before changing behavior.

## Commands

```powershell
docker compose up -d                                   # Azurite; storage is required, the app will not start without it
dotnet build PoWatch.slnx                              # warnings are errors
dotnet run --project src/PoWatch.Api --launch-profile http   # http://localhost (port 80); steps to the next free port if taken
dotnet format PoWatch.slnx --verify-no-changes         # the CI formatting gate
./SCRIPTS/check-hygiene.ps1                            # layout, naming, package versions, vocabulary
./SCRIPTS/check-test-caps.ps1                          # after a build: 100 unit / 50 integration / 25 API E2E / 25 UI E2E
```

Tests, narrowest first (integration and E2E start Azurite through Testcontainers, so Docker must be up):

```powershell
dotnet test tests/PoWatch.Unit --filter "FullyQualifiedName~RollupTests"
dotnet test tests/PoWatch.Integration
dotnet test tests/PoWatch.E2EAPI
$env:E2E_LOCAL = '1'; dotnet test tests/PoWatch.E2EUI -c Release   # local only; needs Playwright's Chromium
```

## Things that are easy to get wrong

- **Sign-in in Development.** Every page needs a session. Open `/auth/login/fake?returnUrl=/` to become
  the guest; `POST /api/dev/seed?days=60&tz=America/New_York` fills it with history (once per user).
- **Layers.** Domain → Application → Infrastructure/Api; Shared is DTOs and browser-safe logic only.
  `ArchitectureBoundaryTests` fails the build on a reference that points the wrong way.
- **The client is trim-analyzed.** Anything serialized in `PoWatch.Client` goes through `PoWatchJsonContext`
  (source-generated); add new DTOs there.
- **Folders.** At most two directory levels under each project; scripts live in `SCRIPTS/`.
- **Pushing to `master` deploys to production.** The deploy gate is `GET /health` returning 200.
