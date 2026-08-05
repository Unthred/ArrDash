# Configuration

ArrDash configuration comes from three layers (later layers override earlier ones for API keys):

1. `appsettings.json` — defaults and dev URLs
2. **Environment variables** — Docker / Unraid template (URLs and optional bootstrap keys)
3. **Secrets backend** (highest priority for keys/URLs):
   - **OpenBao** when `OPENBAO_ADDR`, `OPENBAO_ROLE_ID`, and `OPENBAO_SECRET_ID` are set — KV path `secret/arrdash/media-services`
   - Otherwise **`/config/service-secrets.json`** (local/dev fallback)

Layout and behaviour live in **`/config/user-layout.json`** (managed entirely through Settings).

## OpenBao (production)

When OpenBao is configured, Settings → Save writes service API keys/tokens and optional service URLs to OpenBao. Startup **fails closed** if the vault cannot be read (no silent fallback to the JSON file).

| Variable | Description |
|----------|-------------|
| `OPENBAO_ADDR` | OpenBao base URL (e.g. `https://openbao.yeradonkey.com`) |
| `OPENBAO_ROLE_ID` | AppRole role id |
| `OPENBAO_SECRET_ID` | AppRole secret id (keep out of git; prefer `env_file`) |

House deploy uses `/mnt/user/appdata/arrdash/openbao.env` via compose `env_file`. Trakt **user** OAuth tokens remain encrypted in SQLite (`TraktAccounts`); only the Trakt OAuth **client** id/secret live in the vault. Play webhook token lives at `secret/arrdash/webhook-token` (created on first ArrDash start).

## Config volume

Set `ARRDASH_CONFIG_PATH` (default `/config`). Mount a persistent volume:

```yaml
volumes:
  - ./config:/config
```

| File | Written by | Contents |
|------|------------|----------|
| `user-layout.json` | Settings → Save | Theme, panels, limits, toggles |
| `service-secrets.json` | Settings → Save (only when OpenBao is **not** configured) | API keys and tokens |
| `webhook-token.txt` | Startup (only when OpenBao is **not** configured) | Shared Emby/Plex webhook secret |
| `openbao.env` | Operator | `OPENBAO_*` AppRole credentials (mode 600; not used as app config JSON) |

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
| Jellyfin | `JELLYFIN_URL` | `JELLYFIN_API_KEY` |
| Tautulli | `TAUTULLI_URL` | `TAUTULLI_API_KEY` |
| TMDB | — (fixed API host) | `TMDB_API_KEY` |

### Database (Watch Stats)

| Variable | Default | Description |
|----------|---------|-------------|
| `ARRDASH_DB_PROVIDER` | `Sqlite` | `Sqlite` or `Postgres` |
| `ARRDASH_DB_SQLITE_PATH` | `/config/arrdash.db` | SQLite file on the config volume |
| `ARRDASH_DB_CONNECTION_STRING` | empty | Npgsql connection string when using Postgres |
| `ARRDASH_WATCH_STATS_SYNC_MINUTES` | `20` | Background history sync interval |
| `ARRDASH_WATCH_STATS_BACKFILL_DAYS` | `90` | Initial history backfill depth |
| `ARRDASH_WATCH_STATS_RETENTION_DAYS` | `365` | Delete play events older than this (Trakt-sourced events are exempt — their depth follows the account's history-start setting) |

Optional tuning:

| Variable | Default | Description |
|----------|---------|-------------|
| `POLL_INTERVAL_SECONDS` | `30` | Background refresh interval |
| `RECENT_LIMIT` | `20` | Default fetch cap (settings can override) |

## Host metrics environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ARRDASH_HOST_LABEL` | `Host` | Label shown in metrics bar |
| `ARRDASH_DISK_PATH` | `/` (Linux container) | Path(s) for disk usage; comma-separated for multiple mounts |
| `ARRDASH_PROC_ROOT` | `/proc` | Linux procfs root for CPU and memory |
| `ARRDASH_METRICS_POLL_SECONDS` | `2` | CPU sample interval (Settings can override) |

Settings → **Playback** can override **Host label** and **Disk path(s)** in `user-layout.json` (takes precedence over env when set).

### Cross-platform notes

| Environment | CPU / memory | Disk | Typical config |
|-------------|--------------|------|----------------|
| **Linux Docker** (recommended) | `/proc` inside container | `DriveInfo` on mounted path | `ARRDASH_DISK_PATH=/` or bind-mount host path (e.g. `/mnt/user` on Unraid) |
| **Unraid** | Same | Array pool via mount | `ARRDASH_HOST_LABEL=Unraid`, `ARRDASH_DISK_PATH=/mnt/user`, mount `/mnt/user:ro` |
| **TrueNAS / generic NAS** | Same | Pool mount inside container | Set disk path to your mounted data volume |
| **Windows / macOS Docker Desktop** | Container `/proc` (Linux VM) | Container filesystem unless you bind-mount | Metrics reflect the **Linux container**, not the Windows/macOS host directly |
| **`dotnet run` on Windows** | Not available (no `/proc`) | `DriveInfo` works | Metrics bar hidden when read fails; use Linux container for full metrics |

To show **host** disk on Docker, bind-mount the path you care about and set `ARRDASH_DISK_PATH` (or Settings) to that mount point inside the container.

For Unraid, mount the array path read-only and set `ARRDASH_DISK_PATH` to the mount inside the container (e.g. `/mnt/user`).

## Unraid activity environment variables (optional)

Surfaces *why* CPU/memory might be spiking — an active parity check, mover run, or Docker containers mid-update — next to the CPU graph in the server metrics bar. Both inputs below are independently optional: if a path isn't mounted, that part of the feature is silently disabled rather than erroring.

| Variable | Default | Description |
|----------|---------|-------------|
| `ARRDASH_UNRAID_VAR_INI` | `/var/local/emhttp/var.ini` | Unraid state file for parity-check progress (`mdResync*`) and mover status (`shareMoverActive`) |
| `ARRDASH_DOCKER_SOCKET` | `/var/run/docker.sock` | Docker API socket, used to detect containers in a `restarting`/`created` state (proxy for "currently updating") |

To enable, bind-mount both read-only in `docker-compose.yml`:

```yaml
volumes:
  - /var/local/emhttp/var.ini:/var/local/emhttp/var.ini:ro
  - /var/run/docker.sock:/var/run/docker.sock:ro
```

**Note:** mounting `docker.sock`, even read-only, grants the ArrDash container API visibility into your entire Docker daemon (all containers, not just ArrDash's own). Omit that mount if you don't want that exposure — parity/mover status from `var.ini` alone still works without it.

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
| API keys | OpenBao or `service-secrets.json` | Env vars work until overridden in UI / vault |
| Service URLs | Vault / env / appsettings | Editable in Settings → API keys |
| Poll interval | `user-layout.json` | `0` = use env default |
| Theme, panels, kiosk | `user-layout.json` | Live preview before save |

## URL requirements

- Use URLs reachable **from inside the container** (bridge network).
- Prefer HTTPS hostnames that resolve on your LAN (split DNS) over raw LAN IPs when containers cannot route to `192.168.x.x`.
- ArrDash logs a warning at startup if any configured URL is private/loopback (`ServiceUrlRules`).

## Security notes

- Do **not** commit real API keys or `OPENBAO_SECRET_ID` (use `openbao.env` mode 600 and `docker-compose.example.yml` as template).
- Prefer OpenBao for production secrets; if using `service-secrets.json`, restrict filesystem permissions on `/config`.
- ArrDash has no built-in authentication; put it behind your reverse proxy auth or VPN if exposed beyond LAN.

## Example docker-compose snippet

See [deployment.md](deployment.md) for the full file. Minimal env block (file-based secrets / no OpenBao):

```yaml
environment:
  ARRDASH_CONFIG_PATH: /config
  SONARR_URL: https://sonarr.example.com
  SONARR_API_KEY: your-key-here
  POLL_INTERVAL_SECONDS: 30
volumes:
  - /path/to/appdata/arrdash:/config
```

Production with OpenBao:

```yaml
env_file:
  - /path/to/openbao.env   # OPENBAO_ADDR, OPENBAO_ROLE_ID, OPENBAO_SECRET_ID
environment:
  ARRDASH_CONFIG_PATH: /config
  SONARR_URL: https://sonarr.example.com
  POLL_INTERVAL_SECONDS: 30
```
