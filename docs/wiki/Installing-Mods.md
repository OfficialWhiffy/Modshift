This page explains how to add new mods that you downloaded from a website.

Mods from the **Steam Workshop** don't need to be installed. Subscribe to them in Steam and they
show up in the app by themselves (click **Rescan** if the app was already open).

## Install a mod

1. Download the mod. Mods usually come as a `.scs` file or a `.zip` file.
2. Drag the file onto the Modshift window. You can drop it anywhere.
   You can also click **Install mods…** and pick the file.
3. A window opens with every mod the app found in the file. A single `.zip` can contain several
   mods.
4. For each mod, check the text under its name and the choice on the right (see below).
5. Click **Install**.

You can drop several files at once.

## The choices in the install window

Under each mod's name the app tells you what it found:

| Text | What it means | Usual choice |
|---|---|---|
| New mod. | You don't have this mod yet. | Install |
| Newer version of ... | You have an older version of this mod. | Replace |
| Older than the installed ... | You already have a newer version. | Skip |
| Another version of ... | You have a different version, and the app can't tell which is newer. | Your choice |
| Replaces the installed ... | A file with the same name already exists. | Replace |
| Already installed (identical file). | You already have exactly this file. | Skip |

What the choices do:

- **Install**: put the mod in your `mod` folder.
- **Replace**: put the new mod in your `mod` folder and move the old version to the `mod_archive`
  folder. Nothing is deleted.
- **Skip**: do nothing with this mod.

## The check boxes

At the bottom of the install window you may see two check boxes:

- **Enable the new mods in "your profile"**: also turns the new mods on in the profile you have
  open. You still need to click **Apply changes** afterwards.
- **Update profiles that use a replaced version to the new one**: when you replace a mod, every
  profile that used the old version switches to the new version. The mod keeps its place in the
  load order. Leave this on unless you have a reason not to.

## Where do the files go?

Mods are installed in the game's `mod` folder, for example
`Documents\Euro Truck Simulator 2\mod`. To open it, click **More → Open mod folder**.

You can still copy files into that folder yourself. Click **Rescan** afterwards so the app sees
them.

Next: [Mod order](Mod-Order)
