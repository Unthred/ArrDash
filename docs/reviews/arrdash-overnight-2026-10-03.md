# ArrDash overnight review — 2026-10-03

## Run record

- Started: 2026-10-03T22:43:07+01:00
- Base: `242189d06a581467d0b02e8e06d797c18443c4dd` (`origin/main`)
- Working branch: `feature/issues-84-96-overnight-2026-10-03`
- Scope: issues #84–#96, with duplicate pairs implemented once and kept open for review.
- Baseline: `.NET 10` is required by both project files. The host has no SDK; the isolated
  `mcr.microsoft.com/dotnet/sdk:10.0` test container completed
  `dotnet test tests/ArrDash.Tests/ArrDash.Tests.csproj` successfully (exit 0).
  Restore also reported the pre-existing NU1510 reference warning and NU1903 advisory for
  `SQLitePCLRaw.lib.e_sqlite3` 2.1.10.

## Issue ledger

| Issue | Started | Ended | Disposition | Notes |
| --- | --- | --- | --- | --- |
| #84 | 2026-10-03T22:54:00+01:00 | 2026-10-03T22:58:00+01:00 | implemented and verified | Single-flight dashboard refresh and honest last-success/update-in-progress state. |
| #85 | 2026-10-03T22:49:00+01:00 | 2026-10-03T22:53:00+01:00 | implemented and verified | Explicit upstream credential-expiry state. Browser/app-session expiry is not implementable because ArrDash has no app authentication. |
| #86 | 2026-10-03T22:49:00+01:00 | 2026-10-03T22:53:00+01:00 | implemented and verified | Component failures receive a bounded retry surface; #96 handles Cleanup’s request failure path. |
| #87 | — | — | pending | Storage-blocked triage, building on #7. |
| #88 | — | — | pending | Activity-card usefulness and states. |
| #89 | — | — | pending | Implemented once with duplicate #90. |
| #90 | — | — | duplicate | Exact duplicate of #89; remains open for review. |
| #91 | — | — | pending | Implemented once with duplicate #92. |
| #92 | — | — | duplicate | Exact duplicate of #91; remains open for review. |
| #93 | — | — | pending | User-focused activity drill-down. |
| #94 | — | — | pending | Implemented once with duplicate #95. |
| #95 | — | — | duplicate | Exact duplicate of #94; remains open for review. |
| #96 | 2026-10-03T22:44:00+01:00 | 2026-10-03T22:48:00+01:00 | implemented and verified | One bounded library-scoped snapshot; page-level failure is recoverable with retained prior data and an explicit retry. Related PR #76 remains separate. |
| #98 | — | — | pending | Added to the run; implementation follows #99 investigation. |
| #99 | 2026-10-03T22:58:00+01:00 | — | in progress | Authoritative collection-membership/completion investigation before #98. |

## Review order

1. #96 cleanup candidates
2. #85–#86 auth and recoverable failures
3. #84 dashboard freshness
4. #87 storage triage
5. #91/#92 navigation, then #89/#90 warnings
6. #88 and #93 activity
7. #94/#95 artwork

## Completed work

### #96 — Cleanup Candidates

- Replaced six page-level reads (including whole-history aggregate queries) with one consistent
  analysis snapshot scoped to the on-disk library.
- Added a single-flight guard and a recoverable in-page failure state. The UI never executes a
  cleanup/delete action; existing candidates remain visible after a failed refresh.
- Evidence: the previous `LoadAsync` had no exception boundary, so a repository failure could
  escape the component. This is a component/request failure path, not evidence of a container
  restart. Runtime restart diagnosis remains unverified because the live service was not touched.
- Verified: isolated .NET 10 SDK container ran the full unit suite successfully after the change.

### #85 / #86 — authentication clarity and transient recovery

- Added a global, accessible warning only when a configured offline service reports a sanitized
  authentication-style failure (401, 403, unauthorized/forbidden, or expired token). It links to
  Settings and explicitly says affected data may be stale or unavailable.
- ArrDash has no `AuthenticationStateProvider`, authorization middleware, login route, or app
  cookie. The requested *app-session* expiry behavior therefore remains a product decision rather
  than something this change can simulate honestly.
- Added an `ErrorBoundary` recovery surface and a reusable error page with one user-triggered
  retry (no retry loop) and a dashboard route. The raw exception is not shown to the user.
- Verified: focused classifier tests and the isolated full .NET 10 unit suite passed.

### #84 — dashboard freshness

- The background loop, reconnect path, stale timer, and manual control can all request a refresh.
  They now coalesce around one service-controlled operation; a cancelled caller stops waiting but
  cannot abort the collection shared by other dashboards.
- The hero announces “Updating” and retains the last successful timestamp while work is active.
  The manual button is disabled during that one operation, avoiding overlapping expensive work.
- Verified: `DashboardState` transition coverage plus isolated full .NET 10 unit suite passed.

## Deployment

No production service or configuration was changed. Deployment remains a manual review decision.
