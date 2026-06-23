# ArrDash Blazor — checkpoint (2026-06-22, end of session)

Resume here when continuing work on the media dashboard.

## Live deployment

| Item | Value |
|------|-------|
| URL | https://arrdash.yeradonkey.com (HAProxy → `192.168.13.1:7979`) |
| Container | `arrdash` (Docker Compose) |
| Project | `/mnt/user/projects/arrdash-blazor` |
| Unraid template | `/mnt/cache/cursor-workspace/unraid/docker-templates/my-arrdash.xml` |
| Appdata (layout + secrets) | `/mnt/user/appdata/arrdash` → `/config` |

### Commands

```bash
cd /mnt/user/projects/arrdash-blazor
docker compose build && docker compose up -d
docker logs arrdash -f
curl -s http://127.0.0.1:7979/api/dashboard | jq '.host'
curl -s http://127.0.0.1:7979/health
```

## Session summary (settings + server metrics)

### Settings UI (current tab layout)

Sticky **Save / Discard / Dashboard** toolbar at top (no scrolling to save).

| Tab | Contents |
|-----|----------|
| **Layout** | Display (title, posters, badges) then Panels (order, hide, view) |
| **Appearance** | Panel highlight colours, Theme (mode/density/colours), Background |
| **Lists** | Recent window, limits, time format, audiobook source |
| **Playback** | Now playing, refresh, status bar, startup, **Show server CPU/memory/disk** toggle |
| **Kiosk** | Kiosk mode options |
| **Services** | Enable/disable toggles |
| **API keys** | One service at a time via dropdown + test |

- **Live preview** — edits apply immediately via `LayoutPreferencesService.SetPreview`; Save persists to `user-layout.json` + secrets.
- **Progress bar colour** — removed as separate setting; Now Playing uses the panel highlight colour (`now-playing` accent).

### Server metrics (Unraid host)

Shown below hero when **Playback → Show server CPU, memory & disk** is on (default: on).

| Metric | Source |
|--------|--------|
| CPU | `/proc/stat` — custom SVG scrolling line graph (15 min window, updates each poll + scrolls every 5s) |
| Memory | `/proc/meminfo` — ring gauge + used/free/total text |
| Disk | `DriveInfo` on `/config` mount (`shfs` = array pool) — ring gauge + used/free/total text |

**Env vars** (`docker-compose.yml`):

```yaml
ARRDASH_HOST_LABEL: Unraid
ARRDASH_DISK_PATH: /config
# ARRDASH_PROC_ROOT: /proc   # optional override
```

**Key files:**

- `Services/HostSystemMetricsService.cs` — reads proc + disk
- `Components/Shared/ServerMetricsBar.razor` — SVG CPU graph + ring gauges (MudChart removed — was unreadable)
- `Models/DashboardModels.cs` — `ServerMetrics` on `DashboardSnapshot`
- `wwwroot/css/arrdash.css` — `.server-metrics-*`, `.cpu-live-*`, `.usage-*`

**API:** `GET /api/dashboard` includes `.host` object with cpu/memory/disk fields.

### Possible follow-ups (not done)

- CPU graph: longer window, smoother animation, or true pie charts if rings still not preferred
- Disk path option in Settings UI (currently env only)
- Move CPU sample history to singleton service (survives component remount)
- Secrets migration out of `docker-compose.yml` (deferred earlier)
- Git repo for project (currently **not** a git repo)

## What works (full feature set)

- Blazor Server + MudBlazor UI
- Settings page with tabs above + live preview chip in header when unsaved
- Theme: light/dark/system, density, backgrounds, gradients, button colours, panel accents
- Panels: order, hide, Cards/List/Table per panel
- Now Playing — Plex/Emby sessions, click-through, idle filter
- Recent TV/Movies/Music/Audiobooks — *arr + ABS merge options
- Kiosk: rotation, screensaver, large now playing
- Service status bar (all / offline only / hidden)
- Service credentials in Settings with **Test** (uses form values without save)
- SignalR live refresh + configurable poll interval
- Poster/thumbnail proxy (same-origin)

## Credentials configured (container env)

| Service | Status |
|---------|--------|
| Sonarr | ✅ |
| Radarr | ✅ |
| Chaptarr | ✅ (API may 500 — Chaptarr DB issue) |
| Plex | ✅ |
| AudioBookShelf | ✅ |
| Emby | ❌ key empty |
| Lidarr | ❌ key empty |
| slskd | ❌ key empty |

Env vars in `docker-compose.yml` and Unraid template.

## Known issues

1. **Chaptarr** — occasional `SQLite Error 10: disk I/O error` on history API (Chaptarr side).
2. **Server metrics CPU** — first poll shows flat line until second sample; graph builds over ~15 min window.
3. **Not in git** — consider init + commit when ready.

## Architecture notes

- **Client-facing URLs** — `https://*.yeradonkey.com` only (HAProxy split DNS).
- **Config persistence** — `/config/user-layout.json`, `/config/service-secrets.json`.
- **Preview** — `LayoutPreferencesService` + `ServiceCredentialsPreviewService` overlay env/secrets until Save or Discard.

## Old Python ArrDash (retired)

- Backup: `/mnt/user/projects/arrdash`
