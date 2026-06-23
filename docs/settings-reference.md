# Settings reference

All settings are edited at **/settings**. Changes preview live on the dashboard; **Save** writes to `/config/user-layout.json` and `/config/service-secrets.json`.

Toolbar: **Save** · **Discard** · **Preview dashboard** · **Show help** (inline hints).

---

## Layout tab

### Display

| Setting | Description |
|---------|-------------|
| Dashboard title | Hero heading |
| Hide hero strip | Removes title/subtitle block |
| Subtitle | Hero subtext |
| Poster size | Small / Medium / Large card artwork |
| Poster placement | Left or top relative to text |
| Quality labels | Show format/quality on recent items |
| Episode badges | Season episode row on TV cards |
| Highlight missing episodes | Dashed badge for missing eps; click to search Sonarr |
| Audiobook sync notes | Merge-mode notes between Chaptarr and ABS |

### Panels

For each panel (Now Playing, Recent TV, Movies, Audiobooks, Music):

| Control | Description |
|---------|-------------|
| ↑ ↓ | Reorder |
| Hide | Remove from dashboard |
| View | Cards · List · Table |

---

## Appearance tab

### Theme

| Setting | Description |
|---------|-------------|
| Light / Dark / System | Colour scheme |
| Density | Comfortable vs compact spacing |
| Card corners | Rounded vs sharp |
| Brand mark | 1–3 characters in app bar |
| Background style | Gradient · Solid · Minimal |

Placed directly under the theme toggle when **System** is selected:

| Setting | Description |
|---------|-------------|
| Light background / text | Colours for light mode |
| Dark background / text | Colours for dark mode |

When **Light** or **Dark** alone is selected, only the relevant background and text pickers show.

### Accent colour

Buttons, links, and primary highlights (preset swatches + custom picker).

### Panel colours

Per-panel accent used for headings and panel chrome:

- Now Playing
- Recent TV
- Recent Movies
- Recent Audiobooks
- Recent Music

---

## Lists tab

| Setting | Description |
|---------|-------------|
| Recent window | **Item count** or **Last N days** |
| Recent days | Day window when in days mode |
| Default recent limit | Max items per panel (count mode) |
| Per-panel limits | Override limit per recent panel |
| Time display | Relative · Clock · Date+time (item timestamps) |
| Audiobook source | Merge Chaptarr + ABS · Chaptarr only · ABS only |

---

## Playback tab

| Setting | Description |
|---------|-------------|
| Show Plex sessions | Now Playing includes Plex |
| Show Emby sessions | Now Playing includes Emby |
| Hide idle sessions | Hide paused / 0% sessions |
| Show server CPU, memory & disk | Metrics bar under hero |
| Metrics poll interval | CPU sample rate (0 = 2s default) |
| Metrics graph window | CPU history minutes (0 = 15 min default) |
| Enable click-through | Click items to open source app |
| Deep link click-through | Open specific title vs app home |
| Open links in | New tab · Same tab |
| Missing episode click | Search only · Open Sonarr and search |
| Friendly quality labels | Human-readable quality strings |
| Poll interval | Dashboard refresh seconds (0 = env default) |
| Manual refresh only | Disable automatic polling |
| Status bar | All services · Offline only · Hidden |
| Startup page | Dashboard · Settings |

---

## Kiosk tab

| Setting | Description |
|---------|-------------|
| Auto kiosk on load | Full-screen kiosk at startup |
| Hide hero in kiosk | Cleaner TV layout |
| Large Now Playing | Bigger session cards in kiosk |
| Screensaver | Dim after idle |
| Screensaver minutes | Idle threshold |
| Panel rotation | All panels · Rotate · Now Playing only |
| Rotate seconds | Time per panel when rotating |

Access kiosk via layout route or auto-kiosk setting (see `MainLayout.razor`).

---

## Services tab

Toggle each upstream app **on/off**. Disabled services are not queried and appear as disabled in the status bar.

| Service key | Label |
|-------------|-------|
| sonarr | Sonarr |
| radarr | Radarr |
| chaptarr | Chaptarr |
| audiobookshelf | AudioBookShelf |
| lidarr | Lidarr |
| plex | Plex |
| emby | Emby |

slskd appears in credentials but is not a primary panel source.

---

## API keys tab

Configure one service at a time:

1. Select service from dropdown
2. Set **URL** (must be reachable from container)
3. Paste **API key** (blank field = keep existing saved key)
4. **Test connection** — uses form values without saving
5. **Save** (toolbar) persists all pending changes

Secrets are stored in `service-secrets.json` and override environment variables for keys.

---

## Live preview behaviour

- Layout/appearance/list changes call `ScheduleLivePreviewNow` or debounced preview
- Unsaved state shows **Preview dashboard** chip in toolbar
- **Discard** reloads from disk and clears preview
- Preference changes trigger a dashboard refresh so lists match new filters

---

## Last refresh display

The hero **Last refresh** label uses finer granularity than item timestamps (`42s ago` vs `just now`) and ticks every 10 seconds so auto-poll at 30s does not look stuck.

Item timestamps still follow **Time display** (Relative / Clock / Date+time).
