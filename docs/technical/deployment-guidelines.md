# Deployment Guidelines

Operational guide for deploying PoWatch to Azure App Service (`app-powatch-win`, resource group
`PoWatch`) via GitHub Actions (`.github/workflows/deploy.yml`).

## Pipeline overview

- **Trigger:** push to `master` (or manual `workflow_dispatch`).
- **Build job:** restore → build → `dotnet format --verify-no-changes` → publish → upload artifact.
- **Deploy job:** OIDC `azure/login` → zip deploy → post-deploy health gate (`/health` must return
  200 within ~5 retries).
- Tests are **not** run in CI (Rule 6.4) — they run locally / pre-merge. The health gate is the
  only production verification, so treat it as sacred: never remove or weaken it.

## Emergency deploys

When production is down and the fix must ship immediately:

1. Run the workflow manually: **Actions → CI/CD — Build, Test & Deploy → Run workflow**, set
   `emergency: true`. This skips the formatting gate only — build, publish, and the health gate
   still run.
2. Never delete quality gates to unblock a hotfix; use the emergency path instead, then fix the
   gate violation in a follow-up commit.

Rationale (2026-07-06): a missing final newline in a *test* file blocked every deploy for hours
while production was serving 500.30.

## Diagnosing HTTP 500.30 (app failed to start)

A 500.30 means ANCM could not start (or keep alive) the .NET process. **No in-app endpoint —
`/health`, `/diag/boot`, anything — will respond.** Do not waste time curling them. In order:

1. Download the App Service logs and read the event log — this contains the full startup
   exception with stack trace:

   ```
   az webapp log download -n app-powatch-win -g PoWatch --log-file logs.zip
   # unzip, then read LogFiles/eventlog.xml — look for '.NET Runtime' entries with
   # "The process was terminated due to an unhandled exception."
   ```

2. Live-tail while reproducing: `az webapp log tail -n app-powatch-win -g PoWatch`.
3. Kudu (`https://app-powatch-win.scm.azurewebsites.net`) → Debug console → `LogFiles/` for ANCM
   stdout logs and the event log.

### Known 500.30 causes in this app

| Cause | Signature in eventlog.xml | Fix |
|---|---|---|
| `AzureStorage:ServiceUri` set but the app has no managed identity (or identity lacks storage roles) | `CredentialUnavailableException` from `DefaultAzureCredential`, stack through `AzureStorageInitializer.StartAsync` | Enable system-assigned identity and grant `Storage Table Data Contributor` + `Storage Blob Data Contributor` on the storage account (see below) |
| `EnableKeyVault=true` + `KeyVault:Uri` set without identity / KV role | `CredentialUnavailableException`, stack through `KeyVaultConfiguration.AddPoWatchKeyVault` | Grant the identity `Key Vault Secrets User` on the vault, or unset the flag |
| Missing .NET runtime on the App Service plan | ANCM event: framework not found | Set the runtime stack; note `netFrameworkVersion: v4.0` is a **normal** reading for .NET Core/5+ apps on Windows and does NOT by itself indicate this problem |

### Managed identity setup (one-time, required before MI-based storage/Key Vault config)

```
az webapp identity assign -n app-powatch-win -g PoWatch
PRINCIPAL=$(az webapp identity show -n app-powatch-win -g PoWatch --query principalId -o tsv)
SA_ID=$(az storage account show -n powatchsa -g PoWatch --query id -o tsv)
az role assignment create --assignee "$PRINCIPAL" --role "Storage Table Data Contributor" --scope "$SA_ID"
az role assignment create --assignee "$PRINCIPAL" --role "Storage Blob Data Contributor" --scope "$SA_ID"
az webapp restart -n app-powatch-win -g PoWatch
```

RBAC propagation can take a few minutes; retry `/health` before assuming the fix failed.

## Production configuration changes are deployments

The 2026-07-06 outage was caused by an App Service **settings** change (pointing storage at a
`ServiceUri` for managed-identity auth) made *after* the last healthy deploy, without enabling the
identity. The app crashed on the next recycle with no code having shipped.

- Treat any app-setting change that alters an auth mode, endpoint, or feature flag as a
  deployment: verify `/health` returns 200 immediately after applying it.
- Startup-critical settings (`AzureStorage:ServiceUri`, `FeatureFlags:EnableKeyVault`,
  `KeyVault:Uri`) fail-fast by design — a misconfiguration takes the site down rather than
  soft-degrading. That is intentional; it also means these settings must never be changed
  casually.
- Prefer making such changes via a tracked script/IaC and pairing them with their prerequisite
  (e.g. `ServiceUri` ⇒ identity + role assignments) in the same change.

## Pipeline guards

Any new assertion that can fail the deploy job MUST first be validated against the current
known-good production state: run the probe manually and confirm it *passes* against a healthy app.
If it would have failed a deploy that produced a healthy app, the signal is wrong.

Case study (2026-07-06): a hard gate on `netFrameworkVersion == v10.x` was added mid-incident.
The value legitimately reads `v4.0` for healthy .NET 10 apps on Windows App Service, so the gate
blocked every subsequent deploy during the outage while diagnosing nothing. It is now a warning.

## Pre-deployment checklist

- [ ] `dotnet format PoWatch.slnx --verify-no-changes` passes locally (or the pre-commit hook is
      installed: `git config core.hooksPath .githooks`)
- [ ] Production `/health` currently returns 200 — know your starting state; never deploy onto an
      undiagnosed outage
- [ ] If pipeline files changed: any new guard validated against known-good production
- [ ] If App Service settings changed since the last deploy: prerequisites (identity, roles) are
      in place

## Common deployment pitfalls

- Azure OIDC IDs are repository **secrets** — reference `secrets.*`, not `vars.*` (a `vars.*`
  reference silently resolves empty and `azure/login` fails with "not all values are present").
- `netFrameworkVersion: v4.0` on Windows App Service is a common default reading for .NET Core/5+
  apps — it is not proof the runtime is missing.
- A 500.30 cannot be diagnosed from inside the app: go straight to
  `az webapp log download` → `LogFiles/eventlog.xml`.
- A green deploy is a point-in-time fact — apps can die on the next recycle if config
  prerequisites are missing. Monitor `/health` continuously (App Service Health check +
  Azure Monitor alert), don't rely on the deploy-time gate.
- `az` CLI `webapp config appsettings list` may crash with connection errors on some CLI versions;
  use `az rest` against the ARM `config/appsettings/list` endpoint if needed.
