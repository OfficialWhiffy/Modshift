This page gives you a short tour of the main window and shows you how to turn a mod on.

## The main window

The window has four parts.

**At the top**

- **Game**: choose Euro Truck Simulator 2 or American Truck Simulator. You only see games you have
  installed.
- **Profile**: choose the profile you want to change. A profile is your save: your company, your
  money and your mod list. Each profile has its own mod list.
- **Install mods…**, **Presets…**, **Rescan** and **More**: buttons for the other features.
- **Apply changes**, **Revert** and **Launch game**: on the right side.

**On the left: Available mods**

All mods you have that are *not* turned on in this profile. Use the search box to find a mod by
name, author or category. The menu next to it filters the list:

- **All mods**
- **Local mods**: mods in your `mod` folder.
- **Workshop mods**: mods from the Steam Workshop.
- **With warnings**: mods that may cause problems.

**In the middle: Active mods**

The mods that are turned on in this profile. The game loads them in this order. The mod at the
top has the highest priority. See [Mod order](Mod-Order).

**On the right: details**

Click any mod to see its picture, version, author, description and warnings.

**At the bottom**

The status bar shows what the app just did, whether you have unsaved changes, and whether the
game is running (green dot: not running, red dot: running).

## Turn a mod on

1. Choose your game and profile at the top.
2. Find the mod in **Available mods**.
3. Do one of these:
   - Drag it to **Active mods**.
   - Double-click it.
   - Select it and click the **→** button.
4. The mod is added to the top of the active list, and the status bar shows **Unsaved changes**.
5. Click **Apply changes**.

Your change is saved only when you click **Apply changes**. Until then, you can click **Revert**
to undo everything.

## Turn a mod off

Drag it back to **Available mods**, double-click it, or select it and press **Delete** or the
**←** button. Then click **Apply changes**.

Turning a mod off does not remove its file. It just isn't loaded in this profile anymore.

## Start the game

Click **Launch game**. The game starts through Steam with the mods you chose.

> **Close the game before you change mods.** The game saves your profile when it exits. If you
> change mods while it runs, the game would overwrite your changes. That is why **Apply changes**
> is disabled and a yellow message appears while the game is running.

## Warnings

Some mods show a warning. You can still use them, but read the warning first. Common warnings:

| Warning | What it means |
|---|---|
| Made for game version ... | The mod was made for an older or newer game version. It may still work, or it may cause errors. Check if the author has an update. |
| Needs DLC that is not installed | The mod needs a map or cargo DLC you don't own. It won't work correctly without it. |
| This mod is enabled in the profile but its file is not installed | The profile wants a mod that is gone. See [Fixing and cleaning up](Fixing-and-Cleaning-Up). |
| This folder has no manifest.sii | A folder in your `mod` folder that is not a working mod. See [Fixing and cleaning up](Fixing-and-Cleaning-Up). |
| Several packages share the id ... | You have two copies of the same mod. The game loads only one. Remove the one you don't need. |

Next: [Installing mods](Installing-Mods)
