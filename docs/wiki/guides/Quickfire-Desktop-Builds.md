# Windows helper source builds

Use the root README and `Quickfire.Web.slnf` for the supported web + SQLite quickstart. This guide covers optional Windows helper source builds. It does not qualify signed installers or clean-machine deployment.

## Prerequisites

- SDK 10.0.401 or a permitted servicing patch, Windows 10 build 19041 or later, and WebView2.
- A compatible Visual Studio installation with .NET MAUI/Windows tooling and the .NET Framework 4.8 targeting pack.
- Desktop Office/COM tooling for the retained Tray project. Tray remains a .NET Framework 4.8 Windows executable; it is not a dependency of the web-only solution.

Run the commands below from the repository root in **Visual Studio Developer PowerShell**, where `MSBuild.exe` resolves to Visual Studio's full-framework build engine. Tray's Office COM references require that engine; `dotnet msbuild` is insufficient for the full native solution.

## Build

Publish the bundled web host explicitly into the payload directory:

```powershell
dotnet publish src/Quickfire.Blazor/Quickfire.Blazor.csproj -c Release -r win-x64 --self-contained false -o build/desktop
MSBuild.exe src/Quickfire.Tray/Quickfire.Tray.csproj /restore /t:Build /p:Configuration=Release /p:Platform=AnyCPU
MSBuild.exe src/Quickfire.Desktop/Quickfire.Desktop.csproj /restore /t:Build /p:Configuration=Release
dotnet build src/Quickfire.Call/Quickfire.Call.csproj -c Release
```

Desktop's `PrepareQuickfireHostPackage` target stages `build/desktop` into the MAUI assets and creates the embedded host manifest. The native build output prints the executable paths. Tray's output is `src/Quickfire.Tray/bin/Release/Quickfire.Tray.exe`; Call's framework-dependent output is `src/Quickfire.Call/bin/Release/net10.0/Quickfire.Call.exe`.

The `Quickfire.Shared` project contains reusable bridge contracts and native safety/credential utilities. Web, Desktop, Tray, Call and tests reference this library instead of linking duplicate source files.

## Start and pair

By default, the Desktop launcher extracts its bundled host under `%LOCALAPPDATA%\flashvenom\QuickfireDesktop\openfire-host`, starts the web process and opens its WebView. The configured local port in `appsettings.maui.json` defaults to 5350. SQLite migrations and initialization are handled by the web host. Readiness of its static endpoint is only a launch signal; verify real sign-in and an interactive page before considering startup successful.

**Pair helper → Open server** selects a separately hosted HTTPS server and remembers that choice. **Use local server** returns to the bundled host. Selecting a remote server changes both the displayed application and the helper's pairing context.

Follow [Helper connections](Helper-Connections.md) to pair Desktop, Tray or Call with your own account. Starting a helper no longer grants anonymous Office access. Keep the server and all helper versions together.

If startup fails, inspect the Desktop log under `%LOCALAPPDATA%\flashvenom\QuickfireDesktop\logs`, confirm the payload contains the web executable or DLL, and check the configured port and database access. Back up existing data before making configuration changes. Never delete a database or migration history to work around startup failure.

## Before distributing a desktop build

Build success is separate from interactive MAUI setup, WebView startup, Office COM behavior, signing, installer upgrade/rollback and clean-machine qualification. Run the native smoke checks in the helper guide with synthetic records. Publish only after the applicable release gates are complete.
