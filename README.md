<div align="center">

# Quickfire AMS

#### The insurance agency management system for independent P&C brokers who move fast.

[![Openfire 1.2](https://img.shields.io/badge/Openfire-1.2-ff6a00?style=for-the-badge)](docs/wiki/Release-v1.2.md)   [![.NET 10](https://img.shields.io/badge/.NET-10-7c3aed?style=for-the-badge)](global.json)   [![Web + SQLite](https://img.shields.io/badge/Web-SQLite-1677bc?style=for-the-badge)](#triggerfinger)

[**Quickstart**](#triggerfinger) · [**Video Tour**](https://www.youtube.com/watch?v=ARkqg0iJG0g) · [**Read the Docs**](docs/wiki/Home.md) · [**What's New**](docs/wiki/Release-v1.2.md)

![Quickfire](https://quickfireams.com/images/github/home-small.png)

</div>

## 

## Editions

Openfire is the open source core framework of Quickfire and is focused on workflows. The fully featured, closed source builds for Mac (Desktop only) and Windows (Desktop and Server) are available now at https://quickfireams.com

> **OPENFIRE 1.2 "IGNITION" · RELEASE**
> 
> Openfire is the open source edition, now fully refactored for a cleaner architecture, faster iteration, and a stronger foundation for AI and automation.

## 

![Quickfire](https://quickfireams.com/images/github/renewals-small.png)

## Scope
- **Know your book.** Keep clients, contacts, addresses, locations, policies, and carriers together.
- **Keep renewals moving.** Organize leads, quotes, submissions, tasks, and the next thing that needs doing.
- **Give paperwork a home.** Upload attachments, find the right document, and keep it tied to the right client or policy.
- **Put forms to work.** Manage reusable PDF forms and versions, fill applications, and work with certificates in the built-in editors.
- **Get the details out of your head.** Track tasks, dates, notes, policy limits, rates, and coverages.
- **Keep the team's playbook alive.** Publish procedures in the Company Manual with search, suggested edits, and revision history.
- **Consolidate your APIs** to track payments, phone calls, leads, documents, and forms in one place.
- **Use OpenAI integration** to build custom prompts for data entry, summaries, and workflows.
- **Centralize all the things** from renewals, quotes, leads, and submissions with clear next actions.
- **Talk to your data** in natural language to unlock bleeding edge insights and time savers.
- **Spawn background workers** to handle follow ups, perform routine duties and more.

![Quickfire renewal workspace with tasks and an activity log](https://quickfireams.com/images/github/renewals-small.png)

##

## What's new in 1.2

**The boring stuff got serious attention.**

- **A current foundation:** SDK 10.0.401, ASP.NET Core / EF Core 10.0.12, matching EF tooling, and updated vulnerable dependencies.
- **A first run that can recover:** automatic SQLite migrations and restartable setup, with existing records and passwords preserved.
- **Configuration that behaves:** the web project's `.env` loads early, deployment settings win, and Syncfusion uses your actual license.
- **Helpers with boundaries:** account-owned pairing, expiring credentials, revocation, and one selected Office executor per user.
- **Straight answers from desktop actions:** correlated responses, explicit failures, and no automatic replay when a helper disconnects.
- **A tested starting point:** web-only builds, dependency checks, disposable database tests, and licensed browser smoke tests.

Stale login cookies are cleared when their account no longer exists, and daily tasks handle a missing user safely. Small fixes. Fewer headaches.

[Full 1.2 release notes →](docs/wiki/Release-v1.2.md)

## Loadout

| Under the hood                              | What it does                                                      |
| ------------------------------------------- | ----------------------------------------------------------------- |
| **ASP.NET Core 10 + Blazor**                | Web application with server-side interactivity                    |
| **Entity Framework Core + SQLite**          | Local database, migrations, and the supported 1.2 quickstart      |
| **Microsoft Fluent UI + Syncfusion Blazor** | Interface, grids, forms, and document controls                    |
| **Quickfire.Shared**                        | Shared helper protocol, validation, and credential protection     |
| **Desktop, Tray, and Call**                 | Optional Windows companions for Office actions and incoming calls |

**The supported lane:** Windows-hosted web + SQLite. The web build needs neither MAUI nor Office. SQL Server is experimental; its migration chain, Linux/container hosting, and full installer qualification are separate work.

## Triggerfinger

**Ready. Aim. `dotnet run`.**

Bring Windows, **.NET SDK 10.0.401** (servicing patches are allowed), and your own **Syncfusion license** for the components you use. No OpenAI key is required for core startup.

### 01 · Grab the source

```powershell
git clone https://github.com/flashvenom/Quickfire.git
cd Quickfire
Copy-Item src/Quickfire.Blazor/.env.example src/Quickfire.Blazor/.env
```

### 02 · Set your keys

Edit **`src/Quickfire.Blazor/.env`**:

- Set `ADMIN_PASSWORD` to a unique, strong password. **There is no shared default password.**
- Set `SYNCFUSION` to your license key.
- Keep the example SQLite connection for a local installation.
- The first account defaults to `admin@quickfire.local`. Set `ADMIN_EMAIL` and `ADMIN_USERNAME` if you want to change it.

The web app reads this project-local file, not a `.env` at the repository root. Process environment variables take priority. Keep credentials, databases, and uploaded documents out of Git.

### 03 · Light it up

```powershell
dotnet tool restore
dotnet restore Quickfire.Web.slnf
dotnet build Quickfire.Web.slnf
dotnet dev-certs https --trust
dotnet run --project src/Quickfire.Blazor
```

Open the HTTPS address printed by the app and sign in with your configured account. SQLite migrations and baseline setup run automatically. The default database is **`src/Quickfire.Blazor/local.db`**.

Missing or invalid first-run password? Fix it and restart. Setup resumes safely; later startups preserve existing accounts and passwords.

**Upgrading?** Back up the database and attachments first, then keep your existing connection and storage settings. Follow [Getting started](docs/wiki/Getting-Started.md) for configuration, upgrades, recovery, and storage details.

## Bring your wingmen

The browser handles the core app. Optional Windows helpers bring Office and incoming-call workflows along for the ride.

1. Sign in and open **Profile → Connected helpers**.
2. Create an **Office-helper** or **incoming-call** credential.
3. Enter the server URL and the one-time credential in the updated helper.
4. Select the Office helper that should execute commands. One active executor per account.

Keys expire after **90 days**, can be revoked from Profile, and are protected locally for the current Windows user. Remote connections require trusted HTTPS; actual loopback connections can use HTTP.

Upgrade the server and helpers together. Older anonymous helpers need an upgrade and pairing. Office actions require the corresponding Windows application. Tray stays on .NET Framework 4.8; Call runs on .NET 10.

[Pairing, native builds, and smoke checks →](docs/wiki/guides/Helper-Connections.md)

## Editions

**Same roots. Different loadouts.**

| Edition                  | What you're getting                                                                                                                                  |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Openfire**             | This public repository: the core client, policy, renewal, document, forms, and Company Manual workflows, plus the 1.2 foundation.                    |
| **Quickfire**            | A free, powerful arsenal of tools including private AI, integrations, billing, and advanced automation. Explore it at [quickfireams.com](https://quickfireams.com). |
| **Quickfire Pro**        | Unlock the full potential of Quickfire with more integrations and advanced features. Explore it at [quickfireams.com](https://quickfireams.com). |

![Commercial Quickfire Outreach Center with campaign and engagement views](https://quickfireams.com/images/github/outreach-short-2.png)

*Quickfire Pro's Outreach Center is shown above; it is not included in Openfire.*

## Kick the tires. Send a patch.

```powershell
./build/verify-web.ps1
```

One command restores pinned tooling, audits direct and transitive dependencies, builds the web-only solution, runs the foundation tests, and checks the SQLite migration snapshot. No MAUI or Office required.

The [browser smoke suite](tests/Quickfire.BrowserTests/README.md) covers sign-in, client editing, attachment upload/download, pairing, and stale-session recovery against disposable data. Its separate **Licensed browser smoke** workflow uses the repository secret `QUICKFIRE_BROWSER_SYNCFUSION_LICENSE`; the earlier `OPENFIRE_BROWSER_SYNCFUSION_LICENSE` name remains supported.

Bring focused changes, synthetic test data, and a clear explanation of what got better. Start with [Contributing](CONTRIBUTING.md) and the [architecture guide](docs/wiki/System-Architecture.md).

---

<div align="center">

**Built for the book. Ready for the next renewal.**

[Documentation](docs/wiki/Home.md) · [Release notes](docs/wiki/Release-Notes.md) · [Quickfire Public License](LICENSE.md) · [Commercial Quickfire](https://quickfireams.com)

</div>
