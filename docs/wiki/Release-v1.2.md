# Openfire 1.2 — Reliable Foundation

This update maintains the public .NET 10 foundation and preserves the existing `src`, `docs` and `build` layout.

- Pinned SDK 10.0.401 and matching ASP.NET Core, EF Core and local EF tooling 10.0.12; updated vulnerable dependency chains.
- Web-only solution filter, disposable SQLite and bridge tests, dependency auditing, contributor instructions and a separate licensed browser release gate.
- Early project-local configuration with deployment overrides, explicit database-provider validation and restartable forward-only SQLite initialization.
- Required first-account password, preservation of existing accounts and storage settings, actual Syncfusion configuration and working local browser attachment links.
- Identity-owned helper credentials, one-time key display, 90-day expiry, Profile selection/revocation, scoped calls and Office operations, and protected Windows credential storage.
- Authenticated server dispatch, owner-specific notifications, request correlation, deadlines and explicit failures. Disconnected actions are never replayed.
- Integrated PRs #15 and #16: project-local Syncfusion configuration preserves deployment overrides, and sessions whose account no longer exists return to login with their stale cookie cleared. Daily tasks wait for a matching account.
- Quickfire project and namespace identities, including `Quickfire.Shared` and `Quickfire.Founation.Tests`, preserve Openfire as the public release name. Existing database history, stored desktop locations, pairings and configuration aliases remain compatible.

## Upgrade

Back up the existing SQLite database and attachments before starting the new host. Keep the existing database and storage configuration. Historical migration IDs are unchanged; one forward migration adds helper credentials. Existing accounts do not need a new bootstrap password and are never reset during startup.

Upgrade **server, Desktop, Tray and Call together**, then pair helpers in Profile. Legacy anonymous helpers are rejected. Tray remains on .NET Framework 4.8; Call moves to .NET 10. Office COM and full installer qualification remain separate from the web build.

See [Getting started](Getting-Started.md), [Helper connections](guides/Helper-Connections.md) and the [browser suite](../../tests/Quickfire.BrowserTests/README.md) for configuration and validation instructions. The supported foundation path is Windows-hosted web with SQLite; SQL Server and Linux/container qualification are separate work.

## Release gates

The automatic **Web foundation** workflow must pass. Before publication, also run **Licensed browser smoke** with a valid Syncfusion license and perform the Windows native helper smoke described in the guide. A successful compilation alone does not qualify browser attachments, Office automation, installer signing or clean-machine setup.

## Local verification — September 24, 2026

- `build/verify-web.ps1` passed: 102 tests, direct/transitive dependency auditing, matching EF tooling and no pending model changes. Upgrade fixtures covered both historical Openfire migrations and preserved existing accounts, password hashes, records and storage settings. Added regression checks cover project-local configuration, deployment precedence across old/new setting names and missing-user initialization/rendering.
- All three licensed Chromium tests passed against isolated Production hosts: sign-in, persisted client editing, exact-byte attachment upload/download, deletion, helper pairing/selection/revocation, and removal of a stale account cookie followed by a working login submission. The attachment test also passed three consecutive standalone runs after scoping its input/dialog to the active tab. No horizontal overflow or unhandled JavaScript errors were observed.
- Tray, Call and full-reference Desktop Release builds passed with `Quickfire.Shared` project references. Actual Tray and Call executables and the compiled Desktop bridge authenticated against a disposable real backend, returned correlated results and honored revocation.
- The Windows native smoke created and discarded a synthetic Outlook draft, opened a harmless Word document, returned its contents, rejected an executable path and delivered a synthetic call to its owner. Only the synthetic Office items were closed.

These are local verification results; the GitHub workflows have been added but have not been run on a published commit. Interactive MAUI setup/pairing-window qualification, signed installers, remote deployment, SQL Server and Linux/container qualification remain separate. The release is not published.
