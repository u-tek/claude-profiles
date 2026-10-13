# Claude Profiles

A small Windows app for running as many separate Claude and Codex desktop instances as you like, side by side.
Each profile has its own sign-in, chats, folders and settings.

Unofficial: not made by or affiliated with Anthropic or OpenAI.

## Install

**[⬇ Download ClaudeProfiles-Setup.exe](https://github.com/u-tek/claude-profiles/releases/latest/download/ClaudeProfiles-Setup.exe)** and open it. That's it.

It installs itself for your Windows account (no admin needed), adds Desktop and Start menu shortcuts, and opens.
You only need the [Claude desktop app](https://claude.ai/download) and/or Codex (from the Microsoft Store) installed.
Nothing else, not even .NET. A Codex section appears when Codex is installed.

- **"Windows protected your PC"?** The app isn't code-signed, so SmartScreen warns about it the first time.
  Click **More info → Run anyway**.
- **Updating:** download and open the latest setup. It replaces the installed copy and keeps your profiles.
- **Uninstalling:** Settings → Apps → Claude Profiles → Uninstall. Your profiles' Claude data in
  `%USERPROFILE%\ClaudeProfiles` is kept; delete that folder too if you don't need it.

To build from source instead, you need the .NET 8 SDK:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

## How it works

- **Isolation.** Each profile starts the installed Claude app with its own `--user-data-dir`
  (under `%USERPROFILE%\ClaudeProfiles`). Claude keeps one instance per data folder, so profiles run together and
  always use the current Claude version.
- **Sign-in routing.** Claude's browser sign-in returns through a `claude://` link, and Windows always hands that link
  to the regular Claude app. While a profile is waiting to sign in, the app watches for the short-lived process
  Windows starts for that link, stops it before it reaches the regular app (typically in 100–350 ms), and delivers
  the link to the profile that most recently started a sign-in, which it reads from that profile's `logs\main.log`.
  Keep the app running (it lives in the tray) while signing in.
- **Status.** Running state comes from the process list; sign-in state and account come from each profile's
  `config.json` (`windowSizeWasSignedIn`, `lastKnownAccountUuid`). Two cards on the same account get a warning.

### Codex

- **Isolation.** A Codex profile gets two folders: `codex-home` (its `CODEX_HOME`: sign-in, settings, chats) and
  `user-data` (the app's windows and caches). Codex starts with `CODEX_HOME`, `CODEX_ELECTRON_USER_DATA_PATH` and
  `--user-data-dir` pointing at them, and keeps one instance per data folder.
- **Starting it.** Codex is a Store app, whose exe Windows won't start directly, and its normal launch can't set
  environment variables. So the launcher uses `Invoke-CommandInDesktopPackage` to run itself inside Codex's package
  with `--start-codex`, which sets the variables and starts Codex. Codex keeps its package identity (it needs it for
  its sandbox), and no developer mode is required.
- **Sign-in.** Codex signs in through a localhost callback, which reaches the right window by itself, so no routing is
  needed. A profile counts as signed in when its `codex-home\auth.json` exists.

## Files

| Path | What |
|---|---|
| `%USERPROFILE%\ClaudeProfiles\profiles.json` | Profile list (app, name, colour, data folder) |
| `%USERPROFILE%\ClaudeProfiles\Profiles\<name>` | Each Claude profile's data |
| `%USERPROFILE%\ClaudeProfiles\Profiles\<name> (Codex)` | Each Codex profile's `codex-home` and `user-data` |
| `%LOCALAPPDATA%\Programs\ClaudeProfiles\ClaudeProfiles.exe` | The installed app |
| `%LOCALAPPDATA%\ClaudeProfiles\launcher.log` | Launcher log, including every routed sign-in |

Deleting a profile, or choosing **Start fresh**, moves its data to the Recycle Bin.

## Development

`ClaudeProfiles.exe --screenshot out.png [--dark|--light] [--dialog]` renders the window to a PNG.
`--probe-mode` arms the router for `claude://claude-profiles-probe` test links.

`dotnet publish -c Release -o dist` builds the single self-contained exe. Run from anywhere other than the install
folder, it installs itself; dev builds under `bin\` just run. To release, bump `<Version>` in the `.csproj` and push a
matching tag (`git tag v1.2.0 && git push origin v1.2.0`). The Release workflow builds `ClaudeProfiles-Setup.exe` and
publishes it.
