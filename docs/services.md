# Supported services

ArrDash integrates with the following apps via their HTTP APIs.

## Required for core panels

| Service | Panel / use | Credential | API notes |
|---------|-------------|------------|-----------|
| **Sonarr** | Recent TV | API key | History + series/episode metadata for badges |
| **Radarr** | Recent Movies | API key | History + movie metadata |
| **Chaptarr** | Recent Audiobooks | API key | History filtered to audiobook media type |
| **AudioBookShelf** | Recent Audiobooks (library) | Bearer token | Recent library items; merged with Chaptarr by default |
| **Plex** | Now Playing | X-Plex-Token | Active sessions API |

## Optional

| Service | Use | Credential |
|---------|-----|------------|
| **Lidarr** | Recent Music | API key |
| **Emby** | Now Playing | API key |
| **Jellyfin** | Now Playing | API key |
| **slskd** | Status / future | API key (optional) |
| **Tautulli** | Optional Plex history bootstrap (richer fields) | API key |
| **Tracearr** | Optional Emby/Jellyfin history bootstrap | API key |

## Watch history warehouse (Activity page)

Activity reporting reads **only** the local `PlayEvents` database (`arrdash.db`). Ingest adapters write into it; charts and drilldowns never call Tautulli/Tracearr at query time.

| Setup | Bootstrap ingest | Ongoing without enricher |
|-------|------------------|---------------------------|
| **Plex only** | PMS `/status/sessions/history/all` | Same Plex API |
| **Plex + Tautulli** | Full Tautulli history import | Plex API after you disconnect Tautulli |
| **Emby/JF only** | Playback Reporting plugin (if installed) | Plugin or forward capture |
| **Emby/JF + Tracearr** | Tracearr history import | Tracearr optional after backfill |

Enable **Watch stats sync** in Settings and wait for the first backfill. Tautulli/Tracearr are optional accelerators — not required for ArrDash to run.

**Backfill vs retention:** Settings **Backfill days** (0 = server default, 365) controls how far back the *first* import reaches. **Retention days** (0 = default 365) controls how long events are kept — it must be ≥ your backfill window or older imported rows are pruned on the next sync.

**Libraries:** Activity can include all libraries (default) or a subset chosen in Settings → Watch stats. Picker shows each server's real library names (not hardcoded “TV Shows” / “Movies”). Movies/TV/Music chart buckets still use media type (`movie` / `episode` / `music`), not library titles.

**Emby:** Prefer Tracearr for history bootstrap when configured. Emby Playback Reporting plugin is a fallback; if its playlist API is empty, Tracearr remains the source of truth.

### Trakt (optional history restore)

1. Create an app at [trakt.tv/oauth/applications](https://trakt.tv/oauth/applications) and paste **Client ID** + **Client Secret** under Settings → API keys → Trakt.
2. Settings → Watch stats → **Connect Trakt account**. ArrDash shows an 8-digit PIN — open [trakt.tv/activate](https://trakt.tv/activate), enter the code, and allow access (same flow as other media apps).
3. Map the Trakt account to your ArrDash/Emby/Plex usernames, choose Movies/Episodes and directions (import / push).
4. **Preview** then **Sync now**. Sync is additive only — ArrDash never removes Trakt history or unmarks items.

Imported Trakt plays appear as source `trakt` (and in Combined). Durations from Trakt runtimes are marked estimated.

## Per-service setup

### Sonarr / Radarr / Lidarr / Chaptarr

1. In the app: **Settings → General → Security** → copy API key
2. In ArrDash: set `https://your-host` URL and API key
3. URL must work from the ArrDash container

Chaptarr history may fail if Chaptarr's database is unhealthy — ArrDash shows the error on the status bar (hover red dot).

### AudioBookShelf

1. **Settings → Users → [user] → API keys** (or Admin API keys)
2. Paste the JWT **Bearer token** as `AUDIOBOOKSHELF_API_KEY`

### Plex

1. Obtain `X-Plex-Token` (account settings or authorized device)
2. Set `PLEX_URL` to your server base URL (HTTPS recommended)
3. Set `PLEX_TOKEN`

### Tautulli (optional)

1. In Tautulli: **Settings → Web Interface** → copy API key
2. Set `TAUTULLI_URL` and `TAUTULLI_API_KEY`
3. When configured, ArrDash imports Plex watch history into `PlayEvents` on a schedule (bootstrap). After backfill completes, Activity works from the warehouse; Tautulli is only needed for ongoing enrichment if you want its extra fields.

### Emby / Jellyfin Playback Reporting

1. **Dashboard → Advanced → API Keys** — create or copy an API key
2. Set `EMBY_URL` / `JELLYFIN_URL` and matching API key env vars
3. For **Watch Stats**, install the **Playback Reporting** plugin on each server
4. ArrDash reads live sessions via the standard Emby/Jellyfin API and watch history via the plugin's `user_usage_stats` endpoints

Both use the same session and image API shape; ArrDash treats them as separate sources with independent toggles in Settings → Playback.

## Audiobook merge modes

| Mode | Behaviour |
|------|-----------|
| **Merge** | Combine Chaptarr downloads + ABS library; dedupe by title/author heuristics |
| **Chaptarr only** | Download/history events only |
| **AudioBookShelf only** | Library recently-added only |

Enable **Audiobook sync notes** to show merge hints on cards.

## Posters and thumbnails

ArrDash proxies artwork through same-origin URLs:

| Route | Source |
|-------|--------|
| `/api/poster/sonarr/{seriesId}` | Sonarr |
| `/api/poster/radarr/{movieId}` | Radarr |
| `/api/poster/lidarr/{artistId}` | Lidarr |
| `/api/poster/chaptarr/book/{bookId}` | Chaptarr |
| `/api/poster/audiobookshelf/{itemId}` | ABS |
| `/api/thumbnail/plex?path=…` | Plex |
| `/api/thumbnail/emby/{itemId}` | Emby |
| `/api/thumbnail/jellyfin/{itemId}` | Jellyfin |

This avoids browser mixed-content and Local Network Access issues when the dashboard is served on HTTPS.

## Service health status bar

Each service reports:

| Field | Meaning |
|-------|---------|
| Green dot | Configured and last fetch succeeded |
| Red dot | Configured but unreachable or API error (hover for message) |
| Grey dot | Not configured |

Sonarr, Radarr, and Chaptarr do not show version strings inline; version (if any) may appear on hover for other services.

## Disabling a service

**Settings → Services** toggle off skips HTTP calls entirely. Useful when an app is down for maintenance.

## URL examples

Replace with your own hostnames:

```
SONARR_URL=https://sonarr.example.com
RADARR_URL=https://radarr.example.com
CHAPTARR_URL=https://chaptarr.example.com
AUDIOBOOKSHELF_URL=https://audiobooks.example.com
PLEX_URL=https://plex.example.com
```

Ensure Docker can resolve and reach these URLs.
