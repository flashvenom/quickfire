# Connect a Windows helper

Openfire 1.2 pairs native helpers with the same Identity account used to sign in to the web application. Upgrade the server, Desktop, Tray and Call together. Old anonymous connections are rejected; there is no compatibility switch.

## Pair and select

1. Sign in to Openfire and open **Profile → Connected helpers**.
2. Enter a recognizable helper name and choose **Office and local files** or **Incoming calls**.
3. Select **Create helper key**. Copy the server address and key into your helper. The key is displayed only once; dismiss it after saving.
4. For Office actions, the first Office helper is selected automatically. To switch computers or switch between Desktop and Tray, select **Use for Office actions** beside the intended helper in Profile. An unselected helper cannot execute Office commands.

Use a separate key for each helper. Incoming-call keys cannot receive Office commands or publish command responses. Only the paired account receives its calls. Keys expire after 90 days; create a replacement and revoke the old key. Revocation disconnects live connections and is checked again during operations. Changing the account security stamp, removing the account, or locking it out also invalidates its helpers.

The server address is an origin such as `https://openfire.example`, without a path, query, user name or password. A trusted HTTPS certificate is required outside actual loopback. For local hosting, `http://127.0.0.1:<port>` and `http://localhost:<port>` are supported. Redirects to other origins and certificate-validation bypasses are not supported.

## Desktop and Tray

The Desktop window opens before pairing so you can sign in and create a key. Select **Pair helper** and enter the server address. **Open server** opens that server in the window without requiring a key, so you can sign in and create one in Profile. Return to **Pair helper** to save the Office key. The selected server is shown in the toolbar; **Use local server** returns to the bundled host. **Forget helper** removes the local credential.

For Tray, right-click its notification-area icon and select **Pair with Quickfire…**. Enter the server origin and an Office key. The menu shows its connection state and provides **Reconnect** and **Forget pairing**. Select the matching helper in Profile before trying Office actions.

Desktop and Tray can both be installed, but only the selected helper receives commands. Use Tray for Word document-content access. Office automation requires the appropriate installed desktop Office application and its COM support; the web build has no such requirement.

## Incoming calls

The maintained Call helper requires Windows and .NET 10. Pair it interactively; the key is entered without echo and must not be supplied on the command line:

```powershell
Quickfire.Call.exe --pair
Quickfire.Call.exe '+12025550199' 'Synthetic test caller'
```

Use an **Incoming calls** key, not an Office key. A successful invocation prints `Call notification accepted.` and returns zero. A failure returns nonzero and does not retry. Acceptance means the server accepted a call for the paired account; a closed browser or unavailable notification surface cannot guarantee that somebody saw it.

`Quickfire.Call.exe --forget` removes local pairing. Revoke the corresponding key in Profile to remove server-side access.

## Credential storage and failures

Helpers protect credentials with Windows DPAPI for the current Windows user, bound to the configured server and helper type. Local records are stored under `%LOCALAPPDATA%\Openfire\Native`. Copying a pairing file to another Windows account does not transfer access. **Forget** removes the local file; **Revoke** removes server authorization. Do both when retiring a device.

Office actions carry unique request IDs and expire after 60 seconds. Only the selected device can complete an outstanding request. A disconnected, busy, revoked or expired helper produces an explicit failure. Commands and uncertain results are never automatically replayed. If an action times out after Office has begun processing it, inspect the application before manually trying again.

File-open commands allow ordinary document/image extensions and reject executables, scripts, URI/device paths, alternate data streams and reparse-point paths. A requested document must exist and be accessible on the helper computer. Credentials, document contents and command payloads must not be included in diagnostic logs or issue reports.

## Validation before a release

Run `./build/verify-web.ps1` for authorization, permission, correlation, reconnect and revocation tests. Build all three updated helpers on Windows; Tray stays on .NET Framework 4.8 and the full Desktop solution requires Visual Studio's MAUI/Windows and Office COM tooling.

On an isolated Windows test account/installation, pair each helper with disposable Openfire data. Create an Outlook draft addressed to a synthetic `.invalid` recipient without sending it, open a harmless test document, and publish a synthetic incoming call. Check success and failure in the web UI, revoke a connected helper, and verify that it can no longer act. Do not use production records or close unrelated Office windows. Installer signing, clean-machine installation and full desktop qualification are separate gates.

The dispatcher and connection registry are designed for one web host process. Multiple replicas and distributed execution require separate coordination work.
