# ClipboardManagerX (ClipX)

ClipboardManagerX is a lightweight desktop clipboard history manager built with **Avalonia UI** and **.NET 10**.
It runs in the system tray, keeps your recent copied text items, and lets you quickly re-copy any previous entry.

## Features

- Monitors clipboard text in near real time
- Maintains clipboard history (newest items on top)
- Re-copy an item by selecting it
- Delete individual entries or clear full history
- Clear current system clipboard
- Toggle clipboard monitoring on/off
- Toggle app auto-start on login (Windows/Linux/macOS support in code)
- Minimize-to-tray behavior with tray menu actions
- Light/dark tray and window icon switching based on theme

## Screenshots

### Main window

![ClipboardManagerX main window](Docs/screenshot1.png)

### Theme variants

![ClipboardManagerX dark theme](Docs/dark1.png)
![ClipboardManagerX light theme 1](Docs/white1.png)
![ClipboardManagerX light theme 2](Docs/white2.png)

## Tech Stack

- .NET `net10.0`
- Avalonia UI `12.1.1`
- CommunityToolkit.Mvvm
- FluentIcons.Avalonia

## Prerequisites

- .NET 10 SDK installed
- Linux, Windows, or macOS desktop environment

## Run Locally

From the repository root:

```bash
cd ClipX
dotnet restore
dotnet run
```

## Build (Release)

```bash
cd ClipX
dotnet publish -c Release -f net10.0
```

Published output is placed under `ClipX/bin/Release/net10.0/` (plus runtime-specific folders if you publish with `-r`).

## Project Structure

- `ClipX/` - Avalonia desktop app source
- `ClipX/ViewModels/` - MVVM view models and clipboard logic
- `ClipX/Views/` - UI views (`MainWindow`)
- `ClipX/Assets/` - app and tray icons
- `Docs/` - screenshots and documentation images

## Notes

- Clipboard tracking currently focuses on text items.
- Closing the window hides the app to tray; use tray **Exit** to fully quit.

