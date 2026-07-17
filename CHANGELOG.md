# Changelog

All notable changes to ArrDash are documented here.

Format based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added

- Canonical `PlayEvents` warehouse for Activity reporting — ingest from Tautulli/Plex PMS and Tracearr/Playback Reporting; charts and drilldowns read the local DB only ([#34](https://github.com/Unthred/ArrDash/issues/34))
- Activity page: popular titles, top libraries, peak concurrent streams, library include/exclude picker in Settings ([#34](https://github.com/Unthred/ArrDash/issues/34))
- Service activity icons on status bar chips — live workload from *arr commands/queue, ABS scans, and streaming transcodes ([#5](https://github.com/Unthred/ArrDash/issues/5))
- Activity detection counts **active** work only (`started` commands; `importing`/`downloading` queue items — not `importBlocked`) ([#6](https://github.com/Unthred/ArrDash/issues/6))
- Activity tooltips show data freshness (`Checked Xs ago`) ([#14](https://github.com/Unthred/ArrDash/issues/14))
- Unraid activity awareness in server metrics — parity check / rebuild progress ring, mover and container restart/update badges, disk health, stuck (D-state) processes, top containers by CPU ([#8](https://github.com/Unthred/ArrDash/issues/8))
- CPU history sparkline and expanded server metrics bar (iowait, core temps, disk detail drill-down) ([#12](https://github.com/Unthred/ArrDash/issues/12))
- Hero network throughput rings (↑/↓) from host interface stats ([#11](https://github.com/Unthred/ArrDash/issues/11))
- VPN status badge in hero strip ([#11](https://github.com/Unthred/ArrDash/issues/11))
- Libraries panel — library rollups as their own reorderable/hideable/accent-colourable panel ([#9](https://github.com/Unthred/ArrDash/issues/9))
- Now Playing: per-session LAN/WAN indicator and bandwidth badge on poster; total bandwidth in rolled-up header ([#10](https://github.com/Unthred/ArrDash/issues/10))
- Dashboard client polling (`/api/dashboard`), Blazor circuit reconnect, drag-to-reorder panels ([#13](https://github.com/Unthred/ArrDash/issues/13))
- Service detail panel on status pill click — health, queue breakdown, commands, recent activity ([#21](https://github.com/Unthred/ArrDash/issues/21))
- Jellyfin Now Playing support (sessions API, poster proxy, Settings toggle, API keys tab)
- GitHub workflow: issue template, project board setup, `arrdash-issue-create.sh`, Cursor rules for issue-first development
- Full documentation set under `docs/`

### Changed

- Upgraded to .NET 10 (was .NET 8) — `TargetFramework`, Dockerfile base images, and docs updated ([#32](https://github.com/Unthred/ArrDash/issues/32))
- Repository renamed to [Unthred/ArrDash](https://github.com/Unthred/ArrDash) (was `arrdash-blazor`; old URLs redirect)
- Host metrics: portable defaults (`Host`, disk `/`); Settings overrides for host label and disk path(s); docs for non-Unraid platforms ([#4](https://github.com/Unthred/ArrDash/issues/4))
- `docker-compose.example.yml`: optional Unraid mounts (`var.ini`, `disks.ini`, `docker.sock`) and `ARRDASH_NET_INTERFACE`
- Repository visibility: public

## [Initial]

### Added

- Blazor Server dashboard for Sonarr, Radarr, Chaptarr, Lidarr, AudioBookShelf, Plex, Emby
- Settings UI with live preview, themes, kiosk mode, server metrics
- Docker deployment and Unraid template stub
- Unit tests for settings and theme behaviour
