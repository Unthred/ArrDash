# ArrDash

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**ArrDash** is a [Blazor Server](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) dashboard for homelab media stacks. It aggregates recent downloads from the *arr apps, audiobooks from Chaptarr and AudioBookShelf, music from Lidarr, and live playback from Plex and Emby — in one configurable UI.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![MudBlazor](https://img.shields.io/badge/UI-MudBlazor-594AE2)

## Features

| Area | What you get |
|------|----------------|
| **Recent media** | TV (Sonarr), movies (Radarr), audiobooks (Chaptarr + ABS), music (Lidarr) |
| **Now playing** | Live Plex and Emby sessions with progress |
| **Layout** | Panel order, hide/show, Cards / List / Table per panel |
| **Appearance** | Light / dark / system theme, colours, density, poster size |
| **Kiosk** | Full-screen TV mode, panel rotation, screensaver |
| **Metrics** | Host CPU graph, memory and disk rings, per-service library counts |
| **Settings** | In-app configuration with live preview before save |
| **Refresh** | Background polling + SignalR push to all connected browsers |

## Quick start

```bash
git clone https://github.com/Unthred/arrdash-blazor.git
cd arrdash-blazor
cp docker-compose.example.yml docker-compose.yml
# Edit docker-compose.yml — set service URLs and API keys
docker compose build && docker compose up -d
```

Open **http://localhost:7979**. Further setup (reverse proxy, Unraid, secrets) is in the [deployment guide](docs/deployment.md).

## Documentation

| Doc | Contents |
|-----|----------|
| [docs/README.md](docs/README.md) | Documentation index |
| [architecture.md](docs/architecture.md) | How the app is structured |
| [configuration.md](docs/configuration.md) | Environment variables and config files |
| [deployment.md](docs/deployment.md) | Docker, Unraid, reverse proxy |
| [settings-reference.md](docs/settings-reference.md) | Every Settings tab and option |
| [services.md](docs/services.md) | Supported apps and API requirements |
| [api.md](docs/api.md) | HTTP endpoints |
| [development.md](docs/development.md) | Local dev, tests, contributing |

## Stack

- **.NET 8** — Blazor Server (interactive)
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

Environment variables seed initial URLs and keys; the Settings UI can override and persist secrets without redeploying.

## Tests

```bash
dotnet test tests/ArrDash.Tests/ArrDash.Tests.csproj
```

135+ unit tests cover settings wiring, theme building, filters, and display helpers.

## License

[MIT](LICENSE) — use and modify freely; no warranty.
