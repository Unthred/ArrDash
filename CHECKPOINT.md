# Maintainer notes

Internal checkpoint for ongoing ArrDash work. User-facing documentation lives in **[docs/](docs/README.md)**.

## Repository

- **GitHub:** https://github.com/Unthred/arrdash-blazor (public)
- **Branch:** `main`

## Quick commands

```bash
cd /path/to/arrdash-blazor
docker compose build && docker compose up -d
docker logs arrdash -f
dotnet test tests/ArrDash.Tests/ArrDash.Tests.csproj
curl -s http://127.0.0.1:7979/health
curl -s http://127.0.0.1:7979/api/dashboard | jq '.updatedAt, .host'
```

## Documentation map

| Topic | File |
|-------|------|
| Overview | [README.md](README.md) |
| Architecture | [docs/architecture.md](docs/architecture.md) |
| Config & env | [docs/configuration.md](docs/configuration.md) |
| Deploy | [docs/deployment.md](docs/deployment.md) |
| Settings UI | [docs/settings-reference.md](docs/settings-reference.md) |
| Upstream apps | [docs/services.md](docs/services.md) |
| HTTP API | [docs/api.md](docs/api.md) |
| Dev & tests | [docs/development.md](docs/development.md) |

## Feature checklist (implemented)

- Blazor Server + MudBlazor UI
- Settings with tabs, live preview, Save/Discard
- Theme: light/dark/system, density, backgrounds, panel accents
- Panels: order, hide, Cards/List/Table per panel
- Now Playing — Plex/Emby sessions
- Recent TV/Movies/Music/Audiobooks — *arr + ABS merge
- Kiosk: rotation, screensaver, large now playing
- Service status bar (compact; errors in tooltip)
- Server metrics: CPU graph, memory/disk rings, library rollups
- SignalR live refresh + configurable poll interval
- Poster/thumbnail same-origin proxy
- Unit tests for settings wiring and theme

## Known issues / follow-ups

1. **Chaptarr** — upstream SQLite/disk errors surface as offline status; not an ArrDash bug
2. **CPU graph** — first sample flat until second poll; window fills over ~15 minutes
3. **Secrets in compose** — use `docker-compose.example.yml` in git; keep real keys in local `docker-compose.yml` (gitignored)
4. **Tautulli** — config schema present; not wired to UI yet

## Old Python ArrDash

Retired predecessor; not part of this repository.
