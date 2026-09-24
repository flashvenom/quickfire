# Quickfire browser smoke tests

Each Chromium test uses a fresh copy of a published web app, a temporary SQLite database and randomly generated test-only sign-in credentials. They never connect to an existing installation. The temporary host and files are removed after the test.

Prerequisites: .NET 10, Node.js 20 or newer with npm, and a valid Syncfusion license for Quickfire's full UI flow. Configure `QUICKFIRE_BROWSER_SYNCFUSION_LICENSE` in your environment or CI secret store before running. Do not put a license key in a command, a checked-in file or a test artifact.

From the repository root in PowerShell:

```powershell
dotnet publish src/Quickfire.Blazor/Quickfire.Blazor.csproj -c Release -o artifacts/browser-host
$env:QUICKFIRE_BROWSER_HOST_DIR = (Resolve-Path artifacts/browser-host).Path
Set-Location tests/Quickfire.BrowserTests
npm ci
npx playwright install chromium
npm test
```

On Linux/macOS set `QUICKFIRE_BROWSER_HOST_DIR` to the absolute publish path, then run the same npm commands. Linux CI may need `npx playwright install --with-deps chromium`.

The license is passed only to the disposable host. Without a valid license, Syncfusion can show a full-page dialog with no dismiss button; the tests fail with a specific configuration error instead of altering or bypassing that dialog. An optional `QUICKFIRE_BROWSER_DOTNET` selects a particular dotnet executable.

The older `OPENFIRE_BROWSER_*` environment names remain supported when the corresponding `QUICKFIRE_BROWSER_*` setting is absent.

With Node.js 22, a local repository-root `.env` containing browser-test settings can supply the license without putting the key on the command line. After setting `QUICKFIRE_BROWSER_HOST_DIR` as above, run this from the browser-test directory:

```powershell
node --env-file=../../.env ./node_modules/@playwright/test/cli.js test
```

The root `.env` is ignored by Git. The web application's normal `.env` remains beside `src/Quickfire.Blazor/appsettings.json`; these are separate configuration entry points.

For a licensed CI job, expose the repository's Quickfire-specific license secret as `QUICKFIRE_BROWSER_SYNCFUSION_LICENSE`, publish the web app, set `QUICKFIRE_BROWSER_HOST_DIR` to that absolute output path, then run `npm ci`, `npx playwright install --with-deps chromium` and `npm test` in this directory. Secrets are unavailable to untrusted fork pull requests; run that gate in a trusted, manually dispatched workflow or after reviewing and merging the change. A build/unit-test pass does not replace this licensed browser gate.

Coverage: real sign-in and rendered Blazor navigation, client creation and persisted editing, attachment upload/download byte comparison/deletion, helper key issuance, one-time display, selection and revocation, and a deleted account's existing session returning to a usable login form. Client deletion is not currently offered by Quickfire's client UI; the tests do not invent that feature. These tests do not qualify native Outlook/Word interaction or installers.

Screenshots, traces, video and automatic ARIA failure snapshots are disabled because the pairing flow briefly displays a credential. Test data is synthetic. `npm run test:headed` shows the same test flow for debugging.
