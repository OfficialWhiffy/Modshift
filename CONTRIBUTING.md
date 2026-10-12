# Contributing

Thanks for helping out. This page explains how to build Modshift, run the tests and
publish a release.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the exact version is in
  `global.json`)
- Euro Truck Simulator 2 or American Truck Simulator, only if you want to run the `LocalMachine`
  tests or try the app against real data

## Building and testing

```
dotnet build TruckModsListUpdater.slnx
dotnet test TruckModsListUpdater.slnx --filter "Category!=LocalMachine"
dotnet publish src/TruckModsListUpdater.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The `LocalMachine` tests read a real game install. They never change it, and they are skipped when
the game is not installed. CI does not run them.

## Project layout

| Project | Purpose |
|---|---|
| `src/TruckModsListUpdater.Core` | Game and Steam discovery, SII decoding (ScsC, 3nK, binary, text), HashFS v1/v2 and ZIP readers, mod scanner, profile writer, installer, archive, presets |
| `src/TruckModsListUpdater.App` | WPF user interface (MVVM) |
| `tests/TruckModsListUpdater.Core.Tests` | Unit tests with generated fixtures, plus the `LocalMachine` tests |
| `tests/TruckModsListUpdater.App.Tests` | View model tests |

## How profiles are written

The app changes `profile.sii`, so this part has strict rules:

- It never writes while the game is running. The game rewrites `profile.sii` when it exits, so any
  change made while it runs would be lost.
- Before every change it backs up the profile to `%LOCALAPPDATA%\Modshift\backups`.
- A change is written to a temporary file, read back and checked before it replaces
  `profile.sii`. Only the mod list may differ.
- It refuses to write if the profile changed on disk since it was loaded, for example because of
  Steam Cloud or the game.
- The game's own fallback, `profile.bak.sii`, is never touched.
- Mods are never deleted, only moved to the archive folder.

Profiles are written back as plain text. The game reads these normally and encrypts them again the
next time it saves. Steam Cloud profiles are edited in
`Steam\userdata\<id>\227300\remote\profiles`, which is the file Steam synchronises.

## Releasing

Every merge to `master` publishes a release; there is no version number to edit in the code.
The *Release* workflow takes the latest `vMAJOR.MINOR.PATCH` tag and bumps it, runs the tests,
builds `Modshift-<version>-win-x64.zip`, checks that the executable reports that version
and creates a GitHub release with generated notes. [MinVer](https://github.com/adamralph/minver)
stamps the version into the executable. A build of untagged code reports something like
`2.0.1-alpha.0.7+abc1234`.

The bump depends on the labels of the merged pull request:

- **`major`**: saved data changes incompatibly, or behaviour people rely on is removed.
- **`minor`**: new features.
- no label: **patch**, for bug fixes.

Merges that only touch Markdown files do not release. To publish a specific version or a
pre-release, push a tag instead; a suffix such as `v2.1.0-beta.1` marks it as a pre-release:

```
git tag v2.1.0-beta.1
git push origin v2.1.0-beta.1
```

## Wiki

The wiki pages live in [`docs/wiki`](docs/wiki) so they can be reviewed in pull requests like any
other change. The file names follow GitHub's wiki rules: `Getting-Started.md` becomes the page
*Getting Started*, and `_Sidebar.md` is the menu shown on every page. Links between pages leave out
the `.md`, for example `[Presets](Presets)`, so they work in the wiki but not when browsing
`docs/wiki` in the repository.

GitHub keeps the wiki in its own git repository. To publish the pages:

1. The first time only: open the repository's **Wiki** tab on GitHub and save any page. This
   creates the wiki repository.
2. Copy the pages over and push them:

   ```
   git clone https://github.com/OfficialWhiffy/Modshift.wiki.git
   cp docs/wiki/*.md Modshift.wiki/
   cd Modshift.wiki
   git add .
   git commit -m "Update wiki"
   git push
   ```

Edit the pages in `docs/wiki`, not in the GitHub web editor, or the next copy overwrites the change.
