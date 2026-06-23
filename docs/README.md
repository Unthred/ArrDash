# ArrDash documentation

Complete reference for installing, configuring, and developing ArrDash.

## Getting started

1. [Deployment](deployment.md) — Docker Compose, Unraid template, reverse proxy
2. [Configuration](configuration.md) — environment variables, config volume, secrets
3. [Services](services.md) — connect Sonarr, Radarr, Chaptarr, Plex, etc.

## Using ArrDash

- [Settings reference](settings-reference.md) — every tab and toggle in the Settings UI
- [API](api.md) — health check, dashboard JSON, poster proxy routes

## Development

- [Architecture](architecture.md) — components, services, data flow
- [Development](development.md) — run locally, tests, project layout

## Related files in the repo

| File | Purpose |
|------|---------|
| `README.md` | Project overview |
| `docker-compose.example.yml` | Sample Compose file (no secrets) |
| `Dockerfile` | Multi-stage .NET 8 build |
| `unraid/my-arrdash.xml` | Unraid Community Applications template stub |
| `CHECKPOINT.md` | Maintainer notes (optional) |
