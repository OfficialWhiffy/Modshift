Over time a mod folder fills up with old versions and broken files. This page shows how to fix
missing mods and tidy up.

## Fix missing mods

When you update a mod by hand, the new file often has a different name. For example,
`promods-map-v240` becomes `promods-eu-map-v284`. Your profile still points to the old name, so
the game skips the mod.

The app shows these mods in **Active mods** with a red warning and the source **Not installed**.

To fix them:

1. Click **Fix missing mods** above the active list. The button only appears when there is
   something to fix.
2. The app shows which old mod it will replace with which installed version. Click **Yes**.
3. Click **Apply changes**.

The new version takes the place of the old one in the load order.

If the app can't find a replacement, the mod isn't installed at all. Download and install it
again, or turn it off.

## Clean up the mod folder

The game loads *everything* in the `mod` folder, even old versions and folders that are not
real mods. This can slow down loading and cause errors.

1. Click **More → Clean up mod folder…**.
2. Under **Suggestions** the app lists things you may want to remove, such as:
   - broken folders,
   - older versions of mods you have a newer version of,
   - mods that no profile uses.
3. Tick the ones you want to remove and click **Move selected to archive**.

## The archive

Mods you remove are **not deleted**. They are moved to the `mod_archive` folder next to your
`mod` folder, for example `Documents\Euro Truck Simulator 2\mod_archive`. The game ignores this
folder.

To bring a mod back, open **More → Clean up mod folder…**, select it under **Archive** and click
**Restore**. Click **Open archive folder** to see the files.

If you are sure you don't need the archived mods anymore, you can delete them from the
`mod_archive` folder yourself to free up disk space.

## Remove one mod

Select a mod and click **Move to archive** in the details panel on the right. This works for mods
in your `mod` folder. To remove a Steam Workshop mod, unsubscribe from it in Steam.

Next: [Backups and safety](Backups-and-Safety)
