# Getting started with Openfire 1.2

The supported quickstart is Windows web hosting with SQLite. Start with the commands in the root README. Use `Quickfire.Web.slnf` for web development; the full solution also contains native Windows projects with additional tooling requirements.

## Configuration

Copy `src/Quickfire.Blazor/.env.example` to `.env` in the same directory. Configuration loads before provider and administrator setup; no source edit is necessary. Existing environment-variable names remain supported:

| Setting | Purpose |
| --- | --- |
| `QUICKFIRE_DB` | `Sqlite` (default) or explicitly selected experimental `SqlServer` |
| `DEFAULTCONNECTION` | Database connection; defaults to `Data Source=local.db` |
| `ADMIN_EMAIL`, `ADMIN_USERNAME` | Initial account identity; email defaults to `admin@quickfire.local` |
| `ADMIN_PASSWORD` | Required when the database has no accounts; never resets an existing password |
| `ADMIN_FIRSTNAME`, `ADMIN_LASTNAME`, `ADMIN_PICTURE` | Initial profile values |
| `SYNCFUSION` | Your own component license |
| `QUICKFIRE_DESKTOP`, `QUICKFIRE_DIR` | Desktop launcher markers; avoid these for ordinary web hosting |

Canonical configuration names `Database:Provider`, `ConnectionStrings:DefaultConnection`, `Admin:*`, and `Syncfusion:LicenseKey` are also supported. Environment variables use double underscores for nesting, such as `Database__Provider`. The earlier `OPENFIRE_DB`, `OPENFIRE_DESKTOP`, `OPENFIRE_DIR` and `OPENFIRE_FILESTORAGE_*` names remain accepted for existing deployments. Deployment environment values take priority over project-local `.env` defaults, including when one uses legacy names and the other canonical names. Command-line configuration takes precedence.

The canonical `.env` is beside the web project's `appsettings.json`, including in a published host. Running `dotnet run --project src/Quickfire.Blazor` from the repository root and running `dotnet run` from the project directory use the same configuration and database. No OpenAI key is required.

The application reads `src/Quickfire.Blazor/.env` when running from source, never the repository-root `.env`. Local values do not overwrite process environment variables. Set `SYNCFUSION` or `Syncfusion__LicenseKey` there, or configure the license in the hosting environment; there is no placeholder license. The browser test runner separately supports the repository-root `.env` for its test-only license setting.

## SQLite setup and upgrades

Startup applies pending SQLite migrations, then seeds missing baseline data. Existing migration IDs and records are retained. A missing or invalid first-run password can be corrected and retried even after migrations have already completed. Existing users and passwords are preserved.

Before upgrading an existing installation, stop the host and take a backup of its SQLite database and uploaded files. Keep its connection and storage settings unchanged. Start the new host and check that initialization completed. If a migration fails, stop and inspect the error; do not delete the database or remove migration-history rows. Restore the backup before rolling back to an older binary after a schema upgrade.

Optional explicit migration commands, from the repository root:

```powershell
dotnet tool restore
dotnet ef database update --project src/Quickfire.Blazor
dotnet ef migrations has-pending-model-changes --project src/Quickfire.Blazor
```

These commands use a design-time context and do not launch the web application or seed accounts. Do not manually exclude migration folders. SQL Server has no qualified migration chain in this release; automatic bootstrap is intentionally restricted to SQLite. Invalid providers and connection strings fail clearly without selecting a fallback database.

## Storage and hosting

A new SQLite installation uses its actual web content directory for local attachment storage and same-origin browser links. Existing saved storage settings are preserved. Ensure the service account can write the configured SQLite directory and upload directory. Back up both.

Use trusted HTTPS for a remote host. The development certificate is for local development. Keep secrets in deployment configuration, not command-line arguments, source files, or logs.

## Validation

Run `./build/verify-web.ps1` before submitting changes. Tests use synthetic data and disposable databases. Browser verification must exercise login, client create/edit, and upload/download; a responding health/static URL alone is not proof of a working Blazor circuit.

Optional desktop and Office helper checks are documented in [Helper connections](guides/Helper-Connections.md). Full installer, SQL Server, and Linux qualification are separate from the web foundation.
