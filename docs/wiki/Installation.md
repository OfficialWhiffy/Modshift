This page explains how to download Modshift and start it for the first time.

## What you need

- Windows 10 or 11.
- Euro Truck Simulator 2 or American Truck Simulator, installed through Steam.
- You must have **started the game at least once**. The game creates its folder in
  `Documents` the first time it runs, and the app needs that folder.

## Step 1: Download the app

1. Open the [latest release](https://github.com/OfficialWhiffy/Modshift/releases/latest).
2. Under **Assets**, click `Modshift-<version>-win-x64.zip`.
3. Right-click the downloaded file and choose **Extract All**.
4. Move `Modshift.exe` to a place you like, for example a folder on your desktop.

There is no installer. The app is a single file of about 60 MB, because it includes everything it
needs to run. You don't have to install .NET or anything else.

## Step 2: Start the app

Double-click `Modshift.exe`.

The first time, Windows may show a blue window that says **Windows protected your PC**. This
happens with many small free apps that are not signed with a paid certificate. Click
**More info**, then **Run anyway**.

The app finds your games, Steam Workshop mods and profiles by itself. You don't need to set any
folders.

## Updating

Download the new zip from the [releases page](https://github.com/OfficialWhiffy/Modshift/releases)
and replace the old `Modshift.exe`. Your settings, presets and backups are kept, because
they are stored in a different place (see [Backups and safety](Backups-and-Safety)).

## Removing the app

Delete `Modshift.exe`. To also remove its settings, presets and backups, delete these two
folders. Paste each line in the Explorer address bar to open it:

```
%APPDATA%\Modshift
%LOCALAPPDATA%\Modshift
```

Your mods and profiles are not touched.

Next: [Getting started](Getting-Started)
