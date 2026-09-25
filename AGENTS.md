# AGENTS.md — PoWatch

Rules for any agent working in this repository.

- **Branching.** Do all work on `master`. Use another branch only when specifically asked to.
- **Restart after changes.** After any code change, restart the app and verify it comes back up successfully.
- **Project overview.** Check the `docs/` folder in the repo root for an overall summary of the project.
- **Configuration and secrets.** Do not use `dotnet user-secrets` to store data locally. Put it in
  `appsettings*.json`, or in Azure Key Vault if one exists.
- **Git sync.** When syncing, create a short commit message in casual American slang that reads like
  a human wrote it (not technical), then push.
- **TL;DR.** Any answer longer than 100 words ends with a TL;DR summary of about 20 words.
- **Tests.** Do not run the full test suite after code changes. Run only the tests related to the
  change, or none at all if the change is simple.
- **Automate, don't delegate.** Avoid asking the user to type CLI commands or click through a web
  GUI when you can do it yourself.
- **Warnings are errors.** Treat compile warnings as errors and fix them.
