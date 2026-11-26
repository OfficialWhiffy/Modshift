# Modshift

Manage your Euro Truck Simulator 2 and American Truck Simulator mods without starting the game.

[![Latest release](https://img.shields.io/github/v/release/OfficialWhiffy/Modshift?label=download)](https://github.com/OfficialWhiffy/Modshift/releases/latest)
[![Build](https://img.shields.io/github/actions/workflow/status/OfficialWhiffy/Modshift/ci.yml?branch=master&label=build)](https://github.com/OfficialWhiffy/Modshift/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

<!-- Screenshot: save one as docs/images/main-window.png and uncomment the line below.
![Modshift main window](docs/images/main-window.png)
-->

Modshift shows all your mods in one window. You can turn mods on and off, change their
order, install new ones by dropping files on the window, and save mod lists to use again later.
When you are done, start the game with the right mods already active.

**New here?** Read the [beginner's guide in the wiki](https://github.com/OfficialWhiffy/Modshift/wiki).

## Download

1. Download `Modshift-<version>-win-x64.zip` from the
   [latest release](https://github.com/OfficialWhiffy/Modshift/releases/latest).
2. Unzip it and run `Modshift.exe`. There is no installer.

You need Windows 10 or 11, and you must have started the game at least once.

## Features

- **See every mod you have.** Mods from your `mod` folder and from the Steam Workshop, with their
  icon, version, author and description.
- **Get warnings before you play.** For example when a mod is made for another game version,
  needs DLC you don't own, or its file is missing.
- **Change your mod list outside the game.** Drag mods between *Available* and *Active*, and drag
  them up or down to change their order.
- **Install mods by dropping them on the window.** Works with `.scs` files, `.zip` files and
  downloads that contain several mods. When you install a newer version of a mod you already have,
  the old version is put aside and your profiles switch to the new one.
- **Save presets.** A preset is a saved mod list, such as "ProMods" or "TruckersMP". Apply it to
  any profile, or share it as a `.json` file.
- **Fix missing mods.** If a profile still points to an old mod file, one click switches it to the
  version you have installed.
- **Clean up the mod folder.** Find broken folders, old versions and mods you never use, and move
  them out of the way.
- **Start the game** through Steam with one click.

## Is it safe?

Your profiles are important, so the app is careful with them:

- It makes a backup of the profile before every change. You can go back with
  **More → Restore backup**.
- It never changes anything while the game is running.
- It checks every change before saving it. Only the mod list can change.
- It never deletes mods. Mods you remove are moved to a `mod_archive` folder, and you can put them
  back at any time.

More details are in the wiki: [Backups and safety](https://github.com/OfficialWhiffy/Modshift/wiki/Backups-and-Safety).

## Help

- [Wiki](https://github.com/OfficialWhiffy/Modshift/wiki): step-by-step guides for
  beginners.
- [Troubleshooting](https://github.com/OfficialWhiffy/Modshift/wiki/Troubleshooting):
  answers to common problems.
- [Report a bug or ask a question](https://github.com/OfficialWhiffy/Modshift/issues).
  Please include your game version and, if you can, the log file from
  `%LOCALAPPDATA%\Modshift\logs`.

## For developers

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build the app, run the tests and publish a
release.

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

It is a fan-made tool and is not made
by or connected to SCS Software.
