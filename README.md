<div align="center">

# SOUNDCHECK

A music player for Windows - bilingual, single file, no installer.

[![License: MIT](https://img.shields.io/badge/License-MIT-C8A96E.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Windows-10%2F11-0078D6.svg)]()
[![UI](https://img.shields.io/badge/UI-WPF-2D7D9A.svg)]()

[Русская версия](README.ru.md)

</div>

---

soundcheck is a desktop music player built with WPF on .NET 10. It plays your local
library, keeps everything in a single SQLite file, and ships as one self-contained
`.exe`. The interface is in Russian and English and switches at any time.

## Features

- Crossfade between tracks, shuffle/repeat, and a play queue that survives restarts
- 10-band equalizer with presets and a custom slot
- Playlists: create / rename / delete, set a cover, reorder by dragging, merge, import/export `.m3u`
- Tag editing (title / artist / album + cover art), from the library or the file manager
- File manager with batch rename by tags
- Search, sorting, and drag-and-drop import from Explorer
- Fullscreen "now playing" with a live spectrum
- Stats: listening time, play counts, top tracks, history
- Accent color taken from the album art, blurred background, tray icon, sleep timer, start with Windows

## Shortcuts

| Key | Action |
| --- | --- |
| `Space` | Play / pause |
| `←` / `→` | Previous / next track |
| `Enter` | Play the selected track |
| `Esc` | Close the current panel |

## Build and run

You need Windows and the .NET 10 SDK (only to build - the published app is self-contained).

```
dotnet run -c Debug
```

To produce a single self-contained `.exe`:

```
dotnet publish -c Release
```

It lands at `bin/Release/net10.0-windows/win-x64/publish/soundcheck.exe`. Copy it
anywhere and run - no .NET needed on the machine.

## Data

Library, settings, playlists, stats and history live in one file:

```
%AppData%\SoundCheck\library.db
```

Back it up or restore it from Settings → Data. Audio files aren't touched unless you
edit their tags.

## Built with

WPF on .NET 10, [NAudio](https://github.com/naudio/NAudio) (playback, EQ, FFT),
[TagLibSharp](https://github.com/mono/taglib-sharp) (tags), and
[Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) (library).

## License

MIT - see [LICENSE](LICENSE).
