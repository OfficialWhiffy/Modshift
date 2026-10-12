Your profile holds hours of progress, so Modshift is careful with it. This page explains
what the app does to keep your profiles safe, and how to undo a change.

## What the app does

- **A backup before every change.** Each time you click **Apply changes**, the app first saves a
  copy of the profile.
- **Nothing changes while the game runs.** The game saves the profile when it exits and would
  overwrite any change. So the app waits until the game is closed.
- **Only the mod list changes.** After saving, the app reads the profile back and checks that
  nothing else changed. If something is wrong, the original stays in place.
- **No surprises from other programs.** If the game or Steam Cloud changed the profile after the
  app loaded it, the app stops and asks you to reload it first.
- **Mods are never deleted.** Removed mods go to the `mod_archive` folder. See
  [Fixing and cleaning up](Fixing-and-Cleaning-Up).

The app saves the profile as plain text. The game reads this normally and saves it in its own
format again the next time you play.

## Undo a change

1. Choose the game and profile at the top.
2. Click **More → Restore backup**.
3. Choose a backup from the list. They are sorted by date and time.
4. Click **Yes**.

Before restoring, the app makes a backup of the current state, so you can also undo the restore.

## Where the app keeps its files

Paste a line in the Explorer address bar to open the folder.

| What | Where |
|---|---|
| Backups | `%LOCALAPPDATA%\Modshift\backups` |
| Log files | `%LOCALAPPDATA%\Modshift\logs` |
| Presets | `%APPDATA%\Modshift\presets` |
| Settings | `%APPDATA%\Modshift\settings.json` |

## Steam Cloud profiles

If your profile is saved in the Steam Cloud, the status bar shows **Steam Cloud profile**. The app
can change these profiles too. It edits the file that Steam synchronises.

The first time you save a cloud profile, the app shows a short warning. If Steam later says there
is a **cloud conflict** when you start the game, choose to **keep the local files**.

Next: [Troubleshooting](Troubleshooting)
