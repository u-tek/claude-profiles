# Claude Profiles

A small Windows app for running as many separate Claude desktop instances as you like, side by side.
Each profile has its own sign-in, chats, folders and settings.

Unofficial: not made by or affiliated with Anthropic.

## Install

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

Installs to `%LOCALAPPDATA%\Programs\ClaudeProfiles` and adds Desktop and Start menu shortcuts. Needs the .NET 8 Desktop Runtime and the Claude desktop app.

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

## Files

| Path | What |
|---|---|
| `%USERPROFILE%\ClaudeProfiles\profiles.json` | Profile list (name, colour, data folder) |
| `%USERPROFILE%\ClaudeProfiles\Profiles\<name>` | Each profile's Claude data |
| `%LOCALAPPDATA%\ClaudeProfiles\launcher.log` | Launcher log, including every routed sign-in |

Deleting a profile, or choosing **Start fresh**, moves its data to the Recycle Bin.

## Development

`ClaudeProfiles.exe --screenshot out.png [--dark|--light] [--dialog]` renders the window to a PNG.
`--probe-mode` arms the router for `claude://claude-profiles-probe` test links.
