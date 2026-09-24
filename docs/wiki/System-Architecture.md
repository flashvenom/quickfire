# Openfire architecture

Openfire's public application remains a Blazor Server application organized by feature under `src/Quickfire.Blazor/Domain`. The foundation update does not introduce a new application framework or import private Quickfire services.

## Projects

| Project | Responsibility |
| --- | --- |
| Quickfire.Blazor | ASP.NET Core host, Identity accounts, public workflows, EF Core data, authenticated helper hubs |
| Quickfire.Shared | Reusable class library; its `Bridge` folder holds helper protocol, origin/path validation and Windows credential protection, compatible with the .NET Framework 4.8 Tray helper |
| Quickfire.Founation.Tests | Disposable SQLite, bootstrap, authentication, routing, and helper-safety checks |
| Quickfire.Desktop | Windows MAUI shell and optional Office helper |
| Quickfire.Tray | .NET Framework 4.8 Office/Windows helper |
| Quickfire.Call | .NET 10 incoming-call publisher |

`Quickfire.Web.slnf` selects the web application, shared library and foundation tests. `Quickfire.sln` retains native projects. Call is also built explicitly by CI. Consumers reference `Quickfire.Shared` as a project; shared source files are not linked separately into each executable. Tray remains an executable rather than a dependency of the web host.

## Startup and data

`Infrastructure/Foundation` loads project-local configuration, resolves the provider, and initializes SQLite. `Data/ApplicationDbContextFactory.cs` gives EF tooling a design-time path independent of web startup. Historical migrations remain under `Migrations`; new migrations extend that chain. SQL Server selection remains explicit and experimental; SQLite migrations are never used to bootstrap SQL Server.

An initial administrator requires an explicitly supplied password. Initialization is idempotent and restartable. New storage defaults use the actual web content directory; existing database and attachment locations are preserved.

## Helper connections

Profile manages credentials tied to immutable Identity user IDs. Credentials are scoped to Office execution or incoming-call publication, hashed server-side, expire after 90 days, and can be revoked. The web application dispatches commands directly through authenticated server services; it does not open anonymous SignalR connections back to itself.

`/emberHub` accepts paired Office helpers and correlated responses. `/notificationHub` accepts paired call publishers and delivers calls only to the credential owner. User-selected usernames and arbitrary group joins are not authorization. The selected Office helper receives each command once, and results must match its outstanding request. Revocation, expiry, and account security-state changes invalidate active use.

The Blazor interactive-server circuit is separate from these helper hubs. An unpaired/offline helper cannot prevent browser startup. UI actions report actual helper results instead of claiming success after failed dispatch.

## Compatibility

This is a single-host Windows + SQLite foundation. Helper protocol updates must ship with their server counterpart. Scale-out hosting, SQL Server migrations, Linux PDF-rendering compatibility, and installer qualification require separate work.
