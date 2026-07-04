# HTTP API

ArrDash exposes a small JSON and proxy surface. There is no authentication on these routes — protect access at the reverse proxy if needed.

## Health

```
GET /health
```

Response:

```json
{ "status": "ok", "app": "arrdash" }
```

## Dashboard snapshot

```
GET /api/dashboard
```

Returns the current in-memory `DashboardSnapshot` (same payload pushed over SignalR).

### Top-level fields

| Field | Type | Description |
|-------|------|-------------|
| `recentTv` | array | Recent TV `DownloadItem` list |
| `recentMovies` | array | Recent movies |
| `recentAudiobooks` | array | Recent audiobooks |
| `recentMusic` | array | Recent music |
| `activeSessions` | array | Plex/Emby/Jellyfin now playing |
| `services` | array | Service health chips |
| `updatedAt` | ISO 8601 | Last successful collection time |
| `host` | object? | Server metrics (null if disabled) |

### DownloadItem (abbreviated)

| Field | Description |
|-------|-------------|
| `id` | Stable row id |
| `source` | Enum: Sonarr, Radarr, … |
| `mediaType` | Tv, Movie, Audiobook, Music |
| `title`, `subtitle` | Display text |
| `timestamp` | Event time |
| `posterUrl` | Same-origin proxy path |
| `quality` | Quality string |
| `seasonNumber`, `episodeNumbers` | TV batch info |
| `badgeEpisodeNumbers`, `onDiskEpisodeNumbers`, `unairedEpisodeNumbers` | Episode badge rows |
| `externalUrl` | Deep link to source app |

### ServiceHealth

| Field | Description |
|-------|-------------|
| `key` | e.g. `sonarr` |
| `name` | Display name |
| `configured` | URL + key present |
| `online` | Last fetch succeeded |
| `error` | Error message if offline |
| `version` | App version if reported |

### ActiveSession

| Field | Description |
|-------|-------------|
| `id` | Stable row id, prefixed by server (`plex-`, `emby-`, `jellyfin-`) |
| `server` | Enum: Plex, Emby, Jellyfin |
| `user`, `title`, `subtitle`, `device` | Display text |
| `mediaType` | Upstream media type string |
| `progressPercent` | 0–100 |
| `thumbnailUrl`, `externalUrl` | Same-origin poster proxy / deep link |
| `isLocal` | `true` = LAN, `false` = WAN, `null` if the upstream API didn't expose enough to tell |
| `bitrateKbps` | Source media bitrate in kbps, if known |
| `bandwidthKbps` | Actual current stream bandwidth in kbps (transcode output rate when transcoding, else same as `bitrateKbps`) |

### ServerMetrics (`host`)

| Field | Description |
|-------|-------------|
| `label` | Host label from env |
| `cpuPercent` | Current CPU usage |
| `memoryUsedPercent`, `memoryUsedBytes`, `memoryTotalBytes` | RAM |
| `diskUsedPercent`, `diskUsedBytes`, `diskTotalBytes` | Disk for `ARRDASH_DISK_PATH` |

Unraid activity (parity check / mover / container updates) is not part of the snapshot payload — it's polled directly by `ServerMetricsBar.razor` via `UnraidActivityService`, independent of the main collection cycle.

## Poster and thumbnail proxy

All return image bytes with appropriate content type or 404.

```
GET /api/poster/sonarr/{seriesId}
GET /api/poster/radarr/{movieId}
GET /api/poster/lidarr/{artistId}
GET /api/poster/chaptarr/book/{bookId}
GET /api/poster/chaptarr/author/{authorId}
GET /api/poster/audiobookshelf/{itemId}
GET /api/thumbnail/plex?path={url-encoded-path}
GET /api/thumbnail/emby/{itemId}
GET /api/thumbnail/jellyfin/{itemId}
```

## SignalR

```
/hubs/dashboard
```

Event: **`DashboardUpdated`** — payload is full `DashboardSnapshot`.

Blazor Server also uses its own circuit hub (automatic).

## Blazor pages

| Route | Page |
|-------|------|
| `/` | Dashboard |
| `/settings` | Settings |
| `/Error` | Error boundary |

Kiosk mode is toggled via layout (see `MainLayout.razor`).

## Example: check refresh is working

```bash
curl -s https://arrdash.example.com/api/dashboard | jq '.updatedAt'
sleep 35
curl -s https://arrdash.example.com/api/dashboard | jq '.updatedAt'
```

Timestamps should advance when automatic polling is enabled.
