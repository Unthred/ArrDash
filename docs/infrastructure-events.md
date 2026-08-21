# Infrastructure events JSONL (schema v1)

ArrDash reads infrastructure warnings from a **JSON Lines** file on the Unraid host (default: `/mnt/cache/logs/server-events.jsonl`, mounted read-only into the ArrDash container).

Producers append one JSON object per line. ArrDash replays events chronologically and correlates by `instance` (default: `{source}:{code}`).

Canonical copy also referenced from [ServerMaintenance](https://github.com/Unthred/ServerMaintenance) incident/runbook docs.

## Schema version 1

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schema` | integer | recommended | `1` for this contract |
| `ts` | ISO-8601 UTC | yes | Time of this event |
| `source` | string | yes | Producer id (e.g. `tower-monitor`, `ups`, `opnsense`) |
| `code` | string | yes | Stable warning type (e.g. `load_warning`) |
| `severity` | string | yes | `info`, `warning`, `alert`, `critical` |
| `lifecycle` | string | yes* | `raised`, `updated`, `cleared`, `info` |
| `title` | string | yes | Short human label |
| `message` | string | yes | Current description |
| `detail` | string | no | Machine-oriented context (metrics, paths) |
| `instance` | string | recommended | Correlation id; default `{source}:{code}` |
| `count` | integer | no | Occurrence count while active |
| `firstSeen` | ISO-8601 UTC | no | First `raised` time for this instance |
| `lastSeen` | ISO-8601 UTC | no | Last sample time |
| `active` | boolean | legacy | Deprecated; use `lifecycle`. ArrDash accepts for old lines only |

\* Producers emitting Warning/Alert/Critical **must** use `raised` / `updated` / `cleared`. One-shot informational lines use `lifecycle: info`.

## Lifecycle semantics

| Lifecycle | Meaning |
|-----------|---------|
| `raised` | Condition became active — first occurrence |
| `updated` | Still active; message/detail/count changed (throttle repeated updates) |
| `cleared` | Condition returned to normal — removes from Active in ArrDash |
| `info` | Informational only — never affects Active badge |

ArrDash **does not** time-out well-formed lifecycle warnings. A 2-hour stale rule applies **only** to legacy events that lack `lifecycle` and rely on `active: true`.

## Badge / UI rules

- **Badge count**: active instances with severity `warning`, `alert`, or `critical`
- **Turbo / info**: history only
- **Recently cleared**: `cleared` within 24 hours
- **Dedup**: one row per `instance`; `updated` refreshes last seen + count

## Example lines

```json
{"schema":1,"ts":"2026-08-21T06:55:42Z","source":"tower-monitor","code":"load_warning","severity":"warning","lifecycle":"raised","title":"Load Warning","message":"Load avg: 12.3 (threshold 16).","detail":"load1=12.3 dstate=1","instance":"tower-monitor:load_warning","count":1,"firstSeen":"2026-08-21T06:55:42Z","lastSeen":"2026-08-21T06:55:42Z","active":true}
{"schema":1,"ts":"2026-08-21T07:10:00Z","source":"tower-monitor","code":"load_warning","severity":"warning","lifecycle":"cleared","title":"Load Warning","message":"Resolved: Load Warning","detail":"load1=8.2","instance":"tower-monitor:load_warning","count":5,"firstSeen":"2026-08-21T06:55:42Z","lastSeen":"2026-08-21T07:10:00Z","active":false}
{"schema":1,"ts":"2026-08-21T06:56:00Z","source":"tower-monitor","code":"cpu_power_turbo_peak","severity":"info","lifecycle":"info","title":"CPU Turbo Peak","message":"Package 152W (PL2 excursion).","detail":"pl2=181","instance":"tower-monitor:cpu_power_turbo_peak","active":false}
```

## Producers on Tower today

| Producer | Path | Notes |
|----------|------|-------|
| `server-monitor.sh` | `/boot/config/scripts/server-monitor.sh` | Emits lifecycle for load, D-state, temp, sustained power, RAPL, invalid samples |

Future producers (UPS, OPNsense, disk health) should append to the same file or a directory ArrDash tails — keep `source` + `code` stable.

## ArrDash configuration

| Env / setting | Default |
|---------------|---------|
| `ARRDASH_INFRA_EVENTS_PATH` | `/mnt/cache/logs/server-events.jsonl` |
| Docker mount | `/mnt/cache/logs` → `/mnt/cache/logs` (ro) |

API: `GET /api/infrastructure/events`
