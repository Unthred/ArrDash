# ArrDash documentation

Complete reference for installing, configuring, and developing ArrDash.

## Getting started

1. [Deployment](deployment.md) — Docker Compose, Unraid template, reverse proxy, **auth warning**
2. [Configuration](configuration.md) — environment variables, config volume, secrets
3. [Services](services.md) — connect Sonarr, Radarr, Chaptarr, Plex, etc.

Always install from **`main`** (or the GHCR image built from `main`).

## Using ArrDash

- [Settings reference](settings-reference.md) — every tab and toggle in the Settings UI
- [API](api.md) — health check, dashboard JSON, poster proxy routes

## Development

- [Architecture](architecture.md) — components, services, data flow
- [Development](development.md) — run locally, tests, contributing
- [GitHub workflow](github-workflow.md) — issues and project board (required)
- [GitHub project setup](github-project-setup.md) — one-time board setup
- [Documenting changes](documenting-changes.md) — CHANGELOG and doc checklist
- [AGENTS.md](../AGENTS.md) — Cursor agent rules entry point

## Related files in the repo

| File | Purpose |
|------|---------|
| `README.md` | Project overview |
| `docker-compose.example.yml` | Minimal Compose sample (any Docker host) |
| `docker-compose.unraid.example.yml` | Unraid Compose sample (host metrics mounts) |
| `Dockerfile` | Multi-stage .NET 10 build |
| `unraid/my-arrdash.xml` | Unraid Community Applications template stub |
| `CHECKPOINT.md` | Maintainer notes (optional) |
| `AGENTS.md` | Cursor / agent workflow entry point |
| `scripts/arrdash-issue-create.sh` | Create issue + project board card |
| `scripts/setup-github-arrdash-project.sh` | One-time GitHub project setup |
