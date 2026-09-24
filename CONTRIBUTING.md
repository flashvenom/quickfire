# Contributing to Openfire

Use .NET SDK 10.0.401 or an allowed servicing patch and follow the root README. Run `./build/verify-web.ps1` before a pull request. Web contributions do not require MAUI or Office.

Openfire is the public release name. Projects, assemblies and namespaces use Quickfire, including `Quickfire.Shared` and `Quickfire.Founation.Tests`. Shared helper contracts live in `src/Quickfire.Shared`; that library has no dependency on the Windows Tray executable. Keep persisted identifiers and older configuration aliases compatible when renaming code.

Keep changes focused on Openfire's public code. Do not copy private Quickfire source, prompts, templates, schemas, production migrations, credentials, or user data. Use synthetic test fixtures.

Preserve published migration IDs. Add forward SQLite migrations with repository-local `dotnet ef`, then test fresh initialization and upgrade from existing migrations. Never fix a migration check by suppressing pending-model warnings or deleting database history.

For helper protocol changes, update server, Desktop, Tray, and Call together. Test anonymous rejection, two-user isolation, wrong scopes, revocation of live connections, correlation, and exactly one executor. Keep request parameters, credentials, document contents, and caller details out of logs. Native Office behavior requires a separate Windows smoke test.

The [browser suite](tests/Quickfire.BrowserTests/README.md) verifies rendered user flows on disposable data. Its licensed GitHub workflow is manually triggered before release, using an owner-configured Syncfusion secret; fork pull requests do not receive that secret. Keep this gate distinct from the automatic web foundation checks and the native helper smoke test.

Describe behavior, compatibility impact, and the checks performed in the pull request. The project uses the existing terms in LICENSE.md; this release does not change licensing.
