# Configuration

ArrDash configuration comes from three layers (later layers override earlier ones for API keys):

1. `appsettings.json` — defaults and dev URLs
2. **Environment variables** — Docker / Unraid template
3. **`/config/service-secrets.json`** — keys saved in Settings UI

Layout and behaviour live in **`/config/user-layout.json`** (managed entirely through Settings).

## Config volume

Set `ARRDASH_CONFIG_PATH` (default `/config`). Mount a persistent volume:

```yaml
volumes:
  - ./config:/config
```

| File | Written by | Contents |
|------|------------|----------|
| `user-layout.json` | Settings → Save | Theme, panels, limits, toggles |
| `service-secrets.json` | Settings → Save | API keys and tokens |

Back up this directory before major upgrades.

## Service environment variables

Each service uses a URL and credential env var:

| Service | URL variable | Secret variable |
|---------|--------------|-----------------|
| Sonarr | `SONARR_URL` | `SONARR_API_KEY` |
| Radarr | `RADARR_URL` | `RADARR_API_KEY` |
| Lidarr | `LIDARR_URL` | `LIDARR_API_KEY` |
| Chaptarr | `CHAPTARR_URL` | `CHAPTARR_API_KEY` |
| AudioBookShelf | `AUDIOBOOKSHELF_URL` | `AUDIOBOOKSHELF_API_KEY` |
| slskd | `SLSKD_URL` | `SLSKD_API_KEY` |
| Plex | `PLEX_URL` | `PLEX_TOKEN` |
| Emby | `EMBY_URL` | `EMBY_API_KEY` |
| Tautulli | `TAUTULLI_URL` | `TAUTULLI_API_KEY` |

Optional tuning:

| Variable | Default | Description |
|----------|---------|-------------|
| `POLL_INTERVAL_SECONDS` | `30` | Background refresh interval |
| `RECENT_LIMIT` | `20` | Default fetch cap (settings can override) |

## Host metrics environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ARRDASH_HOST_LABEL` | `Host` | Label shown in metrics bar |
| `ARRDASH_DISK_PATH` | `/` | Path for disk usage (`DriveInfo`) |
| `ARRDASH_PROC_ROOT` | `/proc` | Override for procfs (rare) |
| `ARRDASH_METRICS_POLL_SECONDS` | `2` | CPU sample interval (Settings can override) |

For Unraid, mount the array path read-only and set `ARRDASH_DISK_PATH` to the mount inside the container (e.g. `/mnt/user`).

## appsettings.json

Shipped defaults use placeholder hostnames. Production should set env vars or use Settings:

```json
{
  "MediaServices": {
    "PollIntervalSeconds": 30,
    "RecentLimit": 20,
    "Sonarr": {
      "Url": "https://sonarr.example.com",
      "ApiKey": ""
    }
  }
}
```

## Settings vs environment

| Setting | Where stored | Notes |
|---------|--------------|-------|
| API keys | `service-secrets.json` | Env vars work until overridden in UI |
| Service URLs | Env / appsettings | Editable in Settings → API keys |
| Poll interval | `user-layout.json` | `0` = use env default |
| Theme, panels, kiosk | `user-layout.json` | Live preview before save |

## URL requirements

- Use URLs reachable **from inside the container** (bridge network).
- Prefer HTTPS hostnames that resolve on your LAN (split DNS) over raw LAN IPs when containers cannot route to `192.168.x.x`.
- ArrDash logs a warning at startup if any configured URL is private/loopback (`ServiceUrlRules`).

## Security notes

- Do **not** commit `docker-compose.yml` with real API keys (use `docker-compose.example.yml` as template).
- `service-secrets.json` contains plaintext keys — restrict filesystem permissions on `/config`.
- ArrDash has no built-in authentication; put it behind your reverse proxy auth or VPN if exposed beyond LAN.

## Example docker-compose snippet

See [deployment.md](deployment.md) for the full file. Minimal env block:

```yaml
environment:
  ARRDASH_CONFIG_PATH: /config
  SONARR_URL: https://sonarr.example.com
  SONARR_API_KEY: your-key-here
  POLL_INTERVAL_SECONDS: 30
volumes:
  - /path/to/appdata/arrdash:/config
```
