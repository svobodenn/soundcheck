# Changelog

[Русская версия](CHANGELOG.ru.md)

## v1.3.1 — 2026-05-30

### New
- Playlists now have their own page instead of a cramped side panel, with cover art and drag-to-reorder.
- Explicit tracks get an "E" badge so they're easy to pick out in the list.
- You can rename the actual file from the file manager, not just its tags. Click the pencil on a row, type a name, press Enter.
- The app has a proper icon and logo.
- Two new toggles in Settings.

### Fixes and polish
- Rounded off the corners on the play, pause, skip, shuffle and repeat buttons; the old ones looked too sharp.
- Tidied up the tray menu.

### Performance
- Much lower memory use on big libraries. Cover art isn't held in RAM for every track anymore. It stays in the database and loads on demand, so a library of a few thousand tracks gives back hundreds of megabytes.
- A paused player no longer keeps repainting the progress bar every quarter-second, which trims idle CPU.
