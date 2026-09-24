# Openfire — the public foundation of Quickfire

Openfire is the public edition of Quickfire, an insurance agency management application built with ASP.NET Core 10, Blazor Server, Entity Framework Core, Fluent UI, and Syncfusion.

The 1.2 foundation update focuses on a reproducible Windows web + SQLite installation, maintained dependencies, restartable database setup, and authenticated desktop helpers. Quickfire's commercial AI, integration, billing, and automation features are separate products.

## Start the web application

Requirements: Windows, .NET SDK **10.0.401** (the repository allows servicing patches), and your own Syncfusion license for the components you use. MAUI, Office, SQL Server, and an OpenAI key are not prerequisites for the web build.

```powershell
git clone https://github.com/flashvenom/Quickfire.git
cd Quickfire
Copy-Item src/Quickfire.Blazor/.env.example src/Quickfire.Blazor/.env
```

Edit the new `.env` file. Set `ADMIN_PASSWORD` to a unique strong password and set `SYNCFUSION` to your license key. Keep the example SQLite connection for a local installation. The first account defaults to `admin@quickfire.local`; `ADMIN_EMAIL` and `ADMIN_USERNAME` can override it.

```powershell
dotnet tool restore
dotnet restore Quickfire.Web.slnf
dotnet build Quickfire.Web.slnf
dotnet dev-certs https --trust
dotnet run --project src/Quickfire.Blazor
```

Open the HTTPS address printed by the application and sign in. SQLite migrations and baseline initialization run automatically. If the first password is missing or invalid, correct it and restart. There is no shared default password. Subsequent startup preserves existing accounts and passwords.

The default database is `src/Quickfire.Blazor/local.db`. Configuration is loaded from `.env` beside the web project, independently of the launch working directory. Process environment variables override that file. Do not commit `.env`, databases, or uploaded documents.

See [Getting started](docs/wiki/Getting-Started.md) for configuration, upgrades, recovery, and storage details.

## Verify a contribution

```powershell
./build/verify-web.ps1
```

This restores pinned tooling, audits direct and transitive dependencies, builds the web-only solution, runs isolated foundation tests, and checks the SQLite migration snapshot. It does not require MAUI or Office.

Run the [browser smoke suite](tests/Quickfire.BrowserTests/README.md) against a disposable published host to verify login, client editing, attachments and pairing. The **Licensed browser smoke** GitHub workflow is a separate release gate and requires the repository secret `QUICKFIRE_BROWSER_SYNCFUSION_LICENSE` (the earlier `OPENFIRE_BROWSER_SYNCFUSION_LICENSE` secret is also accepted).

## Connect an optional Windows helper

Sign in, open **Profile → Connected helpers**, and create an Office-helper or incoming-call credential. Enter its server URL and the one-time credential in the updated helper's connection settings. Select the Office helper that should execute commands; only one is active per account.

Credentials expire after 90 days and can be revoked from Profile. Native helpers store them under the current Windows user using protected storage. Remote servers require trusted HTTPS; local desktop hosting can use actual loopback HTTP. Legacy anonymous helpers must be upgraded and paired; there is no compatibility bypass.

See [Helper connections](docs/wiki/guides/Helper-Connections.md). Office operations require the corresponding Windows application; the web quickstart does not.

## Supported scope

- **Foundation target:** Windows-hosted web application with SQLite.
- **Optional helpers:** updated Windows Desktop, .NET Framework 4.8 Tray, and .NET 10 Call; helper protocol and native behavior have separate validation gates.
- **Experimental:** SQL Server. This release does not apply SQLite migrations to SQL Server or supply a SQL Server migration chain.
- Linux/container hosting, full installer qualification, and commercial Quickfire services are outside this release.

The repository retains its existing public client, contact, policy, renewal, form-library, and company-manual features. See [architecture](docs/wiki/System-Architecture.md), [contributing](CONTRIBUTING.md), and [license](LICENSE.md). Commercial Quickfire builds are available at [quickfireams.com](https://quickfireams.com).
