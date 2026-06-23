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
| **slskd** | Status / future | API key (optional) |
| **Tautulli** | Reserved in config | API key (not wired to UI yet) |

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

### Emby

1. **Dashboard → Advanced → API Keys**
2. Set `EMBY_URL` and `EMBY_API_KEY`

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
