# ArrDash

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**ArrDash** is a [Blazor Server](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) dashboard for homelab media stacks. It aggregates recent downloads from the *arr apps, audiobooks from Chaptarr and AudioBookShelf, music from Lidarr, and live playback from Plex, Emby, and Jellyfin — in one configurable UI.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![MudBlazor](https://img.shields.io/badge/UI-MudBlazor-594AE2)

> **No built-in authentication.** ArrDash has no login page. Anyone who can reach the WebUI sees media titles, watch history, API-key Settings, and host metrics. Run it on a **trusted LAN only**, or put it behind your reverse proxy’s auth (Authelia, Authentik, basic auth, Cloudflare Access, etc.). Do **not** expose port 7979 directly to the internet.

## Features

| Area | What you get |
|------|----------------|
| **Recent media** | TV (Sonarr), movies (Radarr), audiobooks (Chaptarr + ABS), music (Lidarr) |
| **Now playing** | Live Plex, Emby, and Jellyfin sessions with progress |
| **Activity** | Watch history warehouse (Plex/Emby/Jellyfin/Trakt) — charts, leaderboards, drilldowns, per-library include/exclude |
| **Trakt** | Multi-account watched-history import (full depth), optional push of new plays, posters via library match or TMDB |
| **Cleanup** | Optional Sonarr/Radarr library cleanup candidates (nav hidden until one is configured) |
| **Layout** | Panel order, hide/show, Cards / List / Table per panel |
| **Appearance** | Light / dark / system theme, colours, density, poster size |
| **Kiosk** | Full-screen TV mode, panel rotation, screensaver |
| **Metrics** | Host CPU graph, memory and disk rings, per-service library counts |
| **Settings** | In-app configuration with live preview before save |
| **Refresh** | Background polling + SignalR push to all connected browsers |

## Quick start

Use the **`main`** branch only (or the published image built from `main`). Feature branches may be incomplete.

```bash
# Prefer the published image (amd64 + arm64)
curl -fsSL -o docker-compose.yml \
  https://raw.githubusercontent.com/Unthred/ArrDash/main/docker-compose.example.yml
docker compose up -d
```

Open **http://localhost:7979**. On first run you’ll see a welcome card — open **Settings → API keys**, add the services you use, Save. Env vars in compose are optional.

**If `docker pull ghcr.io/unthred/arrdash:latest` fails** (package not public yet), build from source instead:

```bash
git clone https://github.com/Unthred/ArrDash.git
cd ArrDash
git checkout main
cp docker-compose.example.yml docker-compose.yml
# In docker-compose.yml: comment out `image:` and uncomment `build: .`
docker compose up -d --build
```

Unraid users who want parity/mover/docker metrics: start from [`docker-compose.unraid.example.yml`](docker-compose.unraid.example.yml) or the template under `unraid/`. Further setup (proxy, secrets) is in the [deployment guide](docs/deployment.md).

## Documentation

| Doc | Contents |
|-----|----------|
| [docs/README.md](docs/README.md) | Documentation index |
| [architecture.md](docs/architecture.md) | How the app is structured |
| [configuration.md](docs/configuration.md) | Environment variables and config files |
| [deployment.md](docs/deployment.md) | Docker, Unraid, reverse proxy, auth |
| [settings-reference.md](docs/settings-reference.md) | Every Settings tab and option |
| [services.md](docs/services.md) | Supported apps and API requirements |
| [api.md](docs/api.md) | HTTP endpoints |
| [development.md](docs/development.md) | Local dev, tests, contributing |
| [github-workflow.md](docs/github-workflow.md) | Issues + project board (required for changes) |
| [AGENTS.md](AGENTS.md) | Cursor agent entry point |

## Stack

- **.NET 10** — Blazor Server (interactive)
- **MudBlazor 7** — UI components
- **SignalR** — live dashboard updates
- **Docker** — recommended deployment

## Service URLs

ArrDash calls each *arr / media app over HTTP from inside the container. Use URLs that resolve **from the container network** — typically HTTPS hostnames on your LAN or split-DNS FQDNs, not `localhost` on the host unless you know routing works.

See [services.md](docs/services.md) for per-app notes.

## Persistence

| Path | Purpose |
|------|---------|
| `/config/user-layout.json` | Theme, layout, behaviour preferences |
| `/config/service-secrets.json` | API keys saved from the Settings UI |
| `/config/arrdash.db` | Watch history warehouse (SQLite default) |

Environment variables seed initial URLs and keys; the Settings UI can override and persist secrets without redeploying.

## Tests

```bash
dotnet test tests/ArrDash.Tests/ArrDash.Tests.csproj
```

240+ unit tests cover settings wiring, theme building, filters, and display helpers.

## License

[MIT](LICENSE) — use and modify freely; no warranty.
