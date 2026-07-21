# Changelog

All notable changes to ArrDash are documented here.

Format based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Changed

- Colleague handoff: minimal `docker-compose.example.yml` (Unraid mounts moved to `docker-compose.unraid.example.yml`); README/deployment stress **`main` only**, GHCR pull + build fallback, and **no built-in auth**
- Unraid template: service URL/key fields optional (configure in Settings on first run); overview notes lack of built-in login

### Added

- Workflow `make-ghcr-public.yml` to set `ghcr.io/unthred/arrdash` visibility to public
- Settings → Diagnostics: configurable log level (Trace through Critical, or "Server default"), takes effect immediately with no restart ([#47](https://github.com/Unthred/ArrDash/issues/47))

### Fixed

- Every catch block across the service/client layer now logs instead of failing silently — makes real problems (a stalled sync, a failed HTTP call, a parse error) visible via `docker logs` instead of requiring a database query to diagnose ([#47](https://github.com/Unthred/ArrDash/issues/47))
- Settings → Playback tab: fields were split into two lopsided columns (14 vs 1) instead of an even 2-column grid ([#48](https://github.com/Unthred/ArrDash/issues/48))
- Settings → Diagnostics: the "Log level" dropdown's label overlapped the selected value until clicked (a MudBlazor floating-label quirk triggered by using an empty string as the "Server default" option's value) ([#48](https://github.com/Unthred/ArrDash/issues/48))
- Settings → Playback tab: "Friendly quality labels" now sits above "Open links in" so each row pairs like-with-like controls (switch+switch, dropdown+dropdown) instead of mismatched types ([#49](https://github.com/Unthred/ArrDash/issues/49))

- Home Activity card overhaul ([#45](https://github.com/Unthred/ArrDash/issues/45)):
  - Watched/Plays/Users/Live now render as bordered stat tiles in a responsive grid instead of naked flex-wrap text
  - Fixed a real image bug: Emby/Jellyfin posters 404'd for anyone using Tracearr without also configuring native Emby/Jellyfin API keys, because the thumb builder ignored the working Tracearr-proxied URL already stored on the row and always built a native-credentials URL instead
  - Recently Watched now groups by exact item **and** user — two people watching the same show show as two distinct rows, each with the episode name; same-user repeats of the same item collapse into one row with a play count, using the native item id (more reliable than episode-title text, which some importers only populate intermittently for the same item)
  - Recently Watched, Top Users, Top Movies, and Top TV are all click-to-drilldown now
  - Season/episode numbers (`SxxEyy`) are now captured on import from Plex (Tautulli and native PMS) and Emby/Jellyfin (Tracearr), not just Trakt, so the Recently Watched subtitle shows them for native plays too — applies to new imports only, existing history isn't retroactively enriched
  - Posters get a second-chance client-side fallback (title search via the media resolver, then initials) when the primary native thumbnail 404s or has gone stale — fixes movies/shows whose Plex art link expired after a server-side re-match

- Network pie slices no longer grow a fat scaled focus ring on click; keyboard focus keeps a thin stroke cue ([#44](https://github.com/Unthred/ArrDash/issues/44))
- Status-bar up/down chips show a spinning sync icon until the first throughput sample lands instead of a misleading `0 B/s` ([#44](https://github.com/Unthred/ArrDash/issues/44))
- Episode names are now persisted on import (were previously discarded) — powers the Recently Watched subtitle and drilldown play list ([#45](https://github.com/Unthred/ArrDash/issues/45))

## [0.1.0] - 2026-07-18

First public release.

### Fixed

- Trakt history no longer silently vanishes: the retention prune skips `trakt`-sourced play events (bounded by the account's history-start instead), and Trakt sync now drops orphaned history links so pruned/deleted events re-import instead of being counted as "already linked" ([#36](https://github.com/Unthred/ArrDash/issues/36))
- Library include/exclude picker actually works now: imports are backfilled with library ids (Emby/Jellyfin via item-path → library-folder mapping, Plex via Tautulli `get_metadata`), exclusions are stored as an excluded-list (new libraries default to visible), and events whose library is not yet known stay visible instead of blanking the Activity page ([#38](https://github.com/Unthred/ArrDash/issues/38))
- Privacy: Trakt push skips plays from excluded libraries — and plays whose library is not yet identified — so hidden libraries are never published to Trakt ([#38](https://github.com/Unthred/ArrDash/issues/38))

### Added

- Release packaging: published image (`ghcr.io/unthred/arrdash`, amd64+arm64) via GitHub Actions, personal defaults scrubbed (VPN badge now behind optional `ARRDASH_VPN_STATUS_URL`, hidden when unset), first-run welcome card, README auth guidance ([#39](https://github.com/Unthred/ArrDash/issues/39), [#40](https://github.com/Unthred/ArrDash/issues/40), [#41](https://github.com/Unthred/ArrDash/issues/41), [#42](https://github.com/Unthred/ArrDash/issues/42))
- Posters for Trakt history items — resolved from your Emby/Jellyfin library by provider id/title and/or TMDB (new API key on the API keys tab), selectable per mode in Settings → Watch stats → Trakt posters; resolved URLs cached on disk ([#37](https://github.com/Unthred/ArrDash/issues/37))
- Canonical `PlayEvents` warehouse for Activity reporting — ingest from Tautulli/Plex PMS and Tracearr/Playback Reporting; charts and drilldowns read the local DB only ([#34](https://github.com/Unthred/ArrDash/issues/34))
- Activity page: popular titles, top libraries, peak concurrent streams, library include/exclude picker in Settings ([#34](https://github.com/Unthred/ArrDash/issues/34))
- Trakt watched-history sync — multi-account OAuth device connect, additive pull into warehouse, optional push of completed plays, preview before sync ([#35](https://github.com/Unthred/ArrDash/issues/35))
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
