Solutions for common problems. If your problem is not here, see [Getting help](#getting-help) at
the bottom.

## The app does not start

- **"Windows protected your PC".** Click **More info**, then **Run anyway**.
- **Nothing happens at all.** Unzip the file first and run `Modshift.exe` from the
  unzipped folder. If it still doesn't start, see [Getting help](#getting-help).

## My game or profile is not listed

- Start the game once and drive or save, then close it. The game creates its folders and
  profiles the first time it runs.
- If you moved your `Documents` folder, for example to OneDrive, the app should still find it.
  If it doesn't, please [report it](https://github.com/OfficialWhiffy/Modshift/issues).

## A mod is not listed

- Click **Rescan**.
- Check the filter next to the search box. Choose **All mods**.
- Check that the file is a `.scs` or `.zip` file and is directly inside the `mod` folder, not in
  a sub-folder. Use **More → Open mod folder** to check.
- For Workshop mods: make sure Steam has finished downloading it.

## "Apply changes" is greyed out

- **The game is running.** Close it completely, including the launcher. The dot in the status bar
  turns green when the game is closed.
- **You have no unsaved changes.** The button only works after you change something.
- **The profile can only be viewed.** See the next question.

## "This profile is saved in binary format and can only be viewed"

The game can save profiles in a binary format that the app can read but not change. To switch to
a format the app can change:

1. Open `Documents\Euro Truck Simulator 2\config.cfg` (or the `American Truck Simulator` folder)
   in Notepad.
2. Find the line `uset g_save_format "..."` and change it to `uset g_save_format "2"`.
3. Save the file, start the game, load the profile and save once.

## "Profile changed"

The profile was changed by something else after the app loaded it, for example by the game or by
Steam Cloud. Click **Yes** to reload it. You need to make your changes again.

## Steam says there is a cloud conflict

This can happen after the app changed a Steam Cloud profile. Choose to **keep the local files**.
See [Backups and safety](Backups-and-Safety).

## The game crashes or a mod doesn't work

- Look at the mod's warnings in the details panel. A mod for another game version is the most
  common cause.
- Check the order. See [Mod order](Mod-Order).
- Turn off half of your mods and test. Keep doing this until you find the mod that causes the
  problem.
- To go back to a mod list that worked, use **More → Restore backup**.

## Getting help

[Open an issue](https://github.com/OfficialWhiffy/Modshift/issues) and include:

- what you did and what happened,
- your game and game version (shown at the top of the window),
- the newest log file from `%LOCALAPPDATA%\Modshift\logs`, if you can.
